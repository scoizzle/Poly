using Poly.DomainModeling.Ontology;

using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Analysis;

// Match arm: arm conditions, value unification and duplicate conditions (#409).

internal sealed partial class ExpressionTypeAnalyzer : INodeAnalyzer {
    /// <summary>
    /// <c>match</c> (#409): every arm condition is Boolean (DMMAT001), the arm values and the
    /// <c>else</c> unify to one type (DMMAT002), and no two arms share a condition (DMMAT003).
    /// Unknown types are skipped, as everywhere else in this pass. Every sub-expression is walked.
    /// </summary>
    private void CheckMatch(
        AnalysisContext context, MatchExpr match,
        Dictionary<string, string> props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        var where = match.Line > 0 ? $" (line {match.Line}, col {match.Col})" : "";
        foreach (var arm in match.Arms) {
            var condition = InferType(context, arm.Condition, props, parameters, enumTypes);
            if (condition.Category is not (TypeCategory.Boolean or TypeCategory.Unknown))
                context.ReportError(match,
                    $"'match' arm condition must be Boolean (got '{Describe(condition)}'){where}.",
                    DomainModelDiagnosticCodes.MatchConditionNotBoolean);
            WalkExpression(context, arm.Condition, props, parameters, enumTypes);
            WalkExpression(context, arm.Value, props, parameters, enumTypes);
            CheckMatchNames(context, match, arm.Condition, props, parameters, enumTypes, where);
            CheckMatchNames(context, match, arm.Value, props, parameters, enumTypes, where);
        }
        WalkExpression(context, match.Else, props, parameters, enumTypes);
        CheckMatchNames(context, match, match.Else, props, parameters, enumTypes, where);

        TypeInfo? reference = null;
        foreach (var value in match.Arms.Select(a => a.Value).Append(match.Else)) {
            var type = InferType(context, value, props, parameters, enumTypes);
            if (type.Category is TypeCategory.Unknown or TypeCategory.Null) continue;
            if (reference is null) { reference = type; continue; }
            if (!Unifies(reference.Value, type))
                context.ReportError(match,
                    $"'match' arm values do not unify: '{Describe(reference.Value)}' and '{Describe(type)}'{where}.",
                    DomainModelDiagnosticCodes.MatchArmTypeMismatch);
        }

        var seen = new List<string>();
        foreach (var arm in match.Arms) {
            var key = StructuralKey(arm.Condition);
            if (seen.Contains(key))
                context.ReportError(match,
                    $"'match' has two arms with the same condition; the later arm is unreachable{where}.",
                    DomainModelDiagnosticCodes.MatchDuplicateCondition);
            else
                seen.Add(key);
        }
    }

    /// <summary>
    /// Two known arm types unify when they share a category (enums: the same enum), or one is an
    /// enum and the other Text, as in comparisons. Not <see cref="Compatible"/>: that treats two
    /// untyped values as equal by name.
    /// </summary>
    private static bool Unifies(TypeInfo a, TypeInfo b) =>
        a.Category == b.Category
            ? a.Category is not TypeCategory.Enum || string.Equals(a.TypeName, b.TypeName, StringComparison.Ordinal)
            : (a.Category is TypeCategory.Enum && b.Category is TypeCategory.Text)
              || (b.Category is TypeCategory.Enum && a.Category is TypeCategory.Text);

    /// <summary>
    /// A position- and identity-free structural key for an expression: type, scalar members (not
    /// <c>Id</c>, <c>Line</c>, <c>Col</c>), then children. Two conditions written on different lines
    /// with the same shape produce the same key.
    /// </summary>
    private static string StructuralKey(Node? node) {
        if (node is null) return "_";
        var scalars = node.GetType().GetProperties()
            .Where(p => p.GetIndexParameters().Length == 0
                && p.Name is not ("Id" or "Line" or "Col" or "Children")
                && (p.PropertyType.IsPrimitive || p.PropertyType.IsEnum
                    || p.PropertyType == typeof(string) || p.PropertyType == typeof(object)
                    || p.PropertyType == typeof(decimal)))
            .Select(p => {
                var value = p.GetValue(node);
                return $"{p.Name}={value?.GetType().Name}:{value}";
            });
        return $"{node.GetType().Name}{{{string.Join(",", scalars)}}}({string.Join(",", node.Children.Select(StructuralKey))})";
    }

    /// <summary>
    /// Fail closed on a bare name that resolves to nothing (it would lower to a member the entity
    /// does not have). Descends only value-level nodes; quantifier and navigation bodies are
    /// target-scoped. A bare name is allowed as a comparison operand against an enum, and as a
    /// value when some enum declares that member.
    /// </summary>
    private void CheckMatchNames(
        AnalysisContext context, MatchExpr match, DomainExpression expr,
        Dictionary<string, string> props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes, string where) {
        switch (expr) {
            case PropertyAccess pa:
                var rewritten = RewriteLibraryIdent(pa);
                if (rewritten is not PropertyAccess
                    || ResolvePropertyType(pa.Name, props, parameters) is not null
                    || string.Equals(pa.Name, "Guid", StringComparison.Ordinal)
                    || enumTypes.Values.Any(e => e.MemberNames.Contains(pa.Name, StringComparer.Ordinal)))
                    return;
                context.ReportError(match,
                    $"'{pa.Name}' in 'match' is not a property, parameter, or enum member{where}.",
                    DomainModelDiagnosticCodes.MatchUnresolvedName);
                return;
            case Comparison cmp:
                CheckMatchNames(context, match, cmp.Left, props, parameters, enumTypes, where);
                CheckMatchNames(context, match, cmp.Right, props, parameters, enumTypes, where);
                return;
            case And or Or or Not or Add or Subtract or Multiply or Divide:
                foreach (var child in expr.Children.OfType<DomainExpression>())
                    CheckMatchNames(context, match, child, props, parameters, enumTypes, where);
                return;
            default:
                return; // nested match reports its own names when it is walked
        }
    }

    /// <summary>The unified value type of a <c>match</c>: the first arm type that is known and not null.</summary>
    private TypeInfo InferMatch(
        AnalysisContext context, MatchExpr match,
        Dictionary<string, string>? props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        var types = match.Arms.Select(a => a.Value).Append(match.Else)
            .Select(v => InferType(context, v, props, parameters, enumTypes)).ToList();
        if (types.Any(t => t.Category is TypeCategory.Unknown))
            return new(TypeCategory.Unknown);
        return types.FirstOrDefault(t => t.Category is not TypeCategory.Null) is { Category: not TypeCategory.Unknown } known
            ? known
            : new(TypeCategory.Null);
    }
}