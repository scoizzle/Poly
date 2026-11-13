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
    /// A condition or arm value the analyzer cannot type is recorded for DMSEM006. Every sub-expression is walked.
    /// </summary>
    private void CheckMatch(
        AnalysisContext context, MatchExpr match,
        Dictionary<string, string> props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        var where = match.Line > 0 ? $" (line {match.Line}, col {match.Col})" : "";
        foreach (var arm in match.Arms) {
            var condition = InferType(context, arm.Condition, props, parameters, enumTypes);
            if (!IsTyped(condition))
                RequireTyped(context, arm.Condition, condition, match, props, parameters, enumTypes);
            else if (Family(condition) is not TypeFamily.Boolean)
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

        ExprType? reference = null;
        foreach (var value in match.Arms.Select(a => a.Value).Append(match.Else)) {
            var type = ArmType(context, value, props, parameters, enumTypes, hint: null);
            if (!IsTyped(type)) {
                RequireTyped(context, value, type, match, props, parameters, enumTypes);
                continue;
            }
            if (Family(type) is TypeFamily.Null) continue;
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
    /// Two known arm types unify when they share a family (an enum, an entity or a value type: the same name), or one
    /// is an enum and the other Text, as in comparisons. By family across the numeric tower until T-09.
    /// </summary>
    private static bool Unifies(ExprType a, ExprType b) =>
        Family(a) == Family(b)
            ? Family(a) is not (TypeFamily.Enum or TypeFamily.Entity or TypeFamily.Value)
              || string.Equals(a.DomainName, b.DomainName, StringComparison.Ordinal)
            : (Family(a) is TypeFamily.Enum && Family(b) is TypeFamily.Text)
              || (Family(b) is TypeFamily.Enum && Family(a) is TypeFamily.Text);

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

    /// <summary>
    /// The unified value type of a <c>match</c>: the first arm type that is known and not null; Unresolved when an arm
    /// has no type. <paramref name="hint"/> is the target's type, which a bare enum member arm takes first (A6).
    /// </summary>
    private ExprType InferMatch(
        AnalysisContext context, MatchExpr match,
        Dictionary<string, string>? props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes, ExprType? hint) {
        var types = match.Arms.Select(a => a.Value).Append(match.Else)
            .Select(v => ArmType(context, v, props, parameters, enumTypes, hint)).ToList();
        if (!types.All(IsTyped))
            return ExprType.Unresolved;
        // A match whose every arm is `null` is typed Text, as it was before the move (an unreachable Null arm).
        return types.Cast<ExprType?>().FirstOrDefault(t => Family(t!.Value) is not TypeFamily.Null) ?? TextFamily;
    }

    /// <summary>
    /// A match arm value's type. A bare name that is no property or parameter is an enum member (A6): of the hint's enum
    /// when it declares the member, else of the one enum that declares it; two or more such enums leave it Unresolved.
    /// </summary>
    private ExprType ArmType(
        AnalysisContext context, DomainExpression value,
        Dictionary<string, string>? props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes, ExprType? hint) {
        var type = InferType(context, value, props, parameters, enumTypes);
        if (type.IsResolved || value is not PropertyAccess { Name: var name }
            || ResolvePropertyType(name, props, parameters) is not null
            || RewriteLibraryIdent(value) is not PropertyAccess)
            return type;
        if (hint is { } target && Family(target) is TypeFamily.Enum && target.DomainName is { } hinted
            && enumTypes.TryGetValue(hinted, out var hintEnum) && hintEnum.MemberNames.Contains(name, StringComparer.Ordinal))
            return target;
        var declaring = EnumsDeclaring(enumTypes, name);
        return declaring.Count == 1 ? ResolveName(context, declaring[0], enumTypes) : type;
    }

    private static List<string> EnumsDeclaring(Dictionary<string, EnumType> enumTypes, string member) =>
        [.. enumTypes.Values.Where(e => e.MemberNames.Contains(member, StringComparer.Ordinal)).Select(e => e.Name)
            .Order(StringComparer.Ordinal)];
}