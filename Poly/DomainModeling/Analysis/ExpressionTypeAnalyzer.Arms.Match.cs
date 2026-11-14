using Poly.DomainModeling.Ontology;

using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using Prim = Poly.Introspection.PrimitiveType;
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
        // Lowering passes a match's slot to every arm value, a nested match included (`Arm` to `AdaptTo` to `LowerMatch`), so a
        // child match takes its parent's slot before it is walked.
        if (context.GetMetadata<MatchTargetMetadata>(match) is { } slot)
            foreach (var value in match.Arms.Select(a => a.Value).Append(match.Else))
                StampMatchTarget(context, value, slot.Target);
        foreach (var arm in match.Arms) {
            var condition = InferType(context, arm.Condition, props, parameters, enumTypes);
            if (!IsTyped(condition))
                RequireTyped(context, arm.Condition, condition, match, props, parameters, enumTypes);
            else if (Family(condition) is not TypeFamily.Boolean)
                context.ReportError(match,
                    $"'match' arm condition must be Boolean (got '{Describe(condition)}'){where}.",
                    DomainModelDiagnosticCodes.MatchConditionNotBoolean);
            StampOptionalBoolean(context, arm.Condition, condition);
            WalkExpression(context, arm.Condition, props, parameters, enumTypes);
            WalkExpression(context, arm.Value, props, parameters, enumTypes);
            CheckMatchNames(context, match, arm.Condition, props, parameters, enumTypes, where);
            CheckMatchNames(context, match, arm.Value, props, parameters, enumTypes, where);
        }
        WalkExpression(context, match.Else, props, parameters, enumTypes);
        CheckMatchNames(context, match, match.Else, props, parameters, enumTypes, where);

        var armHint = context.GetMetadata<MatchTargetMetadata>(match)?.Target;
        var unification = UnifyMatch(context, match, props, parameters, enumTypes, hint: null, target: armHint);
        foreach (var (value, type) in match.Arms.Select(a => a.Value).Append(match.Else).Zip(unification.ArmTypes)) {
            if (!IsTyped(type))
                RequireTyped(context, value, type, match, props, parameters, enumTypes);
        }
        // A string literal beside an enum member names a member of the slot's enum, as the plain `Mood == "Nope"` requires (the
        // generated C# cannot type `Flag ? Mood.Happy : "Nope"`): the plain form's own message and code, at the match (a string
        // literal from the DSL carries no position).
        if (unification.AllTyped && unification.Mismatches.Count == 0 && Family(unification.Unified) is TypeFamily.Enum
            && unification.Unified.DomainName is { } slotEnum && armHint is { } slotType && Family(slotType) is TypeFamily.Enum
            && string.Equals(slotType.DomainName, slotEnum, StringComparison.Ordinal))
            foreach (var value in match.Arms.Select(a => a.Value).Append(match.Else))
                if (value is Literal { Value: string literal })
                    CheckEnumMember(context, match, slotEnum, literal, enumTypes);
        foreach (var (first, second) in unification.Mismatches)
            context.ReportError(match,
                $"'match' arm values do not unify: '{DescribeArm(first)}' and '{DescribeArm(second)}'{where}.",
                DomainModelDiagnosticCodes.MatchArmTypeMismatch);
        if (unification.AllTyped && unification.Mismatches.Count == 0 && context.GetMetadata<MatchTypingMetadata>(match) is null)
            context.SetMetadata(match, new MatchTypingMetadata(unification.Unified, unification.ArmTypes));

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
    /// What a <c>match</c>'s arms unify to (T-09a): the one function <c>CheckMatch</c> (the verdict and the stamp) and
    /// <c>InferMatch</c> (the type) both read, so they cannot differ. <see cref="Unified"/> is the first known non-null arm
    /// type when the arms do not unify (so nothing downstream cascades), optional when an arm is <c>null</c> or optional.
    /// </summary>
    private sealed record MatchUnification(
        ExprType Unified, IReadOnlyList<ExprType> ArmTypes, IReadOnlyList<(ExprType First, ExprType Second)> Mismatches, bool AllTyped);

    /// <summary>
    /// Every known arm type is merged into one: the same family (an enum, an entity or a value type: the same name), the
    /// same primitive, or a Decimal with an integer arm (<c>long</c> to <c>decimal</c> is exact, and lowering casts the
    /// integer arm). A Decimal with a Float, Date with DateTime, and an enum with a Text that is not a string literal into an
    /// enum slot do not unify (DMMAT002). An optional arm is no obstacle: lowering fills an optional local, and a <c>long?</c>
    /// arm assigned into it converts as a plain assignment does, on every host.
    /// </summary>
    private MatchUnification UnifyMatch(
        AnalysisContext context, MatchExpr match,
        Dictionary<string, string>? props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes, ExprType? hint, ExprType? target) {
        var values = match.Arms.Select(a => a.Value).Append(match.Else).ToList();
        var types = values.Select(v => ArmType(context, v, props, parameters, enumTypes, hint)).ToList();
        if (!types.All(IsTyped))
            return new MatchUnification(ExprType.Unresolved, types, [], AllTyped: false);

        var optional = types.Any(t => t.Optional);
        var mismatches = new List<(ExprType, ExprType)>();
        ExprType? unified = null;
        var firstKnown = types.Cast<ExprType?>().FirstOrDefault(t => Family(t!.Value) is not TypeFamily.Null);
        foreach (var type in types.Where(t => Family(t) is not TypeFamily.Null)) {
            if (unified is null) { unified = type.AsOptional(false); continue; }
            if (MergeArmTypes(unified.Value, type) is { } merged) unified = merged;
            else mismatches.Add((unified.Value, type));
        }
        if (unified is { } element && mismatches.Count == 0) {
            if (Family(element) is TypeFamily.Enum && values.Zip(types).Any(pair => Family(pair.Second) is TypeFamily.Text)
                && !(target is { } slot && Family(slot) is TypeFamily.Enum
                     && string.Equals(slot.DomainName, element.DomainName, StringComparison.Ordinal)
                     && values.Zip(types).All(pair => Family(pair.Second) is not TypeFamily.Text || pair.First is Literal { Value: string })))
                mismatches.Add((element, TextFamily));
        }

        // A Decimal that came from unnamed arithmetic (`Dec * 2`) takes the Decimal name where the slot is a Number that is not a
        // Decimal, so the name rule of `Compatible` keeps it out of that slot whichever arm comes first. Into a declared Decimal
        // alias (`Cents`, API only) it stays unnamed and is accepted as on the base (the arithmetic itself is T-07's).
        if (unified is { Definition.PrimitiveType: Prim.Decimal, DomainName: null } unnamed
            && target is { } numberSlot && Family(numberSlot) is TypeFamily.Number && numberSlot.Definition?.PrimitiveType is not Prim.Decimal)
            unified = unnamed with { DomainName = "Decimal" };
        // A match whose every arm is `null` is typed Text, as it was before the move (an unreachable Null arm).
        var result = mismatches.Count > 0 ? firstKnown!.Value.AsOptional(false) : unified ?? TextFamily;
        return new MatchUnification(result.AsOptional(optional), types, mismatches, AllTyped: true);
    }

    private static bool IsIntegerClass(ExprType type) => type.Definition?.PrimitiveType is Prim.Int32 or Prim.Int64;

    /// <summary>
    /// Two known, non-null arm types merged, or null when they do not unify: an enum with Text (the caller decides the
    /// slot), Decimal with an integer arm (Decimal), and otherwise one family and one primitive.
    /// </summary>
    private static ExprType? MergeArmTypes(ExprType a, ExprType b) {
        if (Family(a) != Family(b))
            return Family(a) is TypeFamily.Enum && Family(b) is TypeFamily.Text ? a
                : Family(b) is TypeFamily.Enum && Family(a) is TypeFamily.Text ? b.AsOptional(false)
                : null;
        if (Family(a) is TypeFamily.Enum or TypeFamily.Entity or TypeFamily.Value)
            return string.Equals(a.DomainName, b.DomainName, StringComparison.Ordinal) ? a : null;
        if (Family(a) is not TypeFamily.Number)
            return a.Definition?.PrimitiveType == b.Definition?.PrimitiveType ? a : null;
        var (left, right) = (a.Definition?.PrimitiveType, b.Definition?.PrimitiveType);
        if (left == right) return a;
        if (left is Prim.Decimal && IsIntegerClass(b)) return a;
        if (right is Prim.Decimal && IsIntegerClass(a)) return b.AsOptional(false);
        return null;
    }

    /// <summary>An arm type as DMMAT002 names it: a number by its real type (<see cref="Describe"/> keeps one word for the family).</summary>
    private static string DescribeArm(ExprType type) {
        var name = Family(type) is TypeFamily.Number
            ? type.Definition?.PrimitiveType switch {
                Prim.Int64 => "Number",
                Prim.Int32 => "Int32",
                Prim.Float32 => "Float32",
                Prim.Float64 => "Float",
                _ => Describe(type),
            }
            : Describe(type);
        return type.Optional ? name + " optional" : name;
    }

    /// <summary>
    /// Stamps the slot a <c>match</c> value is assigned into on the match itself, so that its verdict (an enum arm with a
    /// Text arm) can read it when the match is walked. Only the first stamp stays; any other value is left alone.
    /// </summary>
    private static void StampMatchTarget(AnalysisContext context, DomainExpression value, ExprType target) {
        if (value is MatchExpr match && IsTyped(target) && context.GetMetadata<MatchTargetMetadata>(match) is null)
            context.SetMetadata(match, new MatchTargetMetadata(target));
    }

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
    /// The unified value type of a <c>match</c> (<see cref="UnifyMatch"/>): the type its arms unify to, optional when an arm
    /// is <c>null</c> or optional; Unresolved when an arm has no type. <paramref name="hint"/> is the target's type, which a
    /// bare enum member arm takes first (A6) and which an enum and Text arm unify into.
    /// </summary>
    private ExprType InferMatch(
        AnalysisContext context, MatchExpr match,
        Dictionary<string, string>? props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes, ExprType? hint) =>
        UnifyMatch(context, match, props, parameters, enumTypes, hint, target: hint ?? context.GetMetadata<MatchTargetMetadata>(match)?.Target).Unified;

    /// <summary>
    /// The first <c>null</c> arm value of <paramref name="value"/> when it is a <c>match</c>, nested arms included:
    /// the position a <c>null</c> arm into a required target is reported at (#606). Null otherwise.
    /// </summary>
    private static Literal? NullArm(DomainExpression value) =>
        value is not MatchExpr match
            ? null
            : match.Arms.Select(a => a.Value).Append(match.Else)
                .Select(arm => arm is Literal { Value: null } literal ? literal : NullArm(arm))
                .FirstOrDefault(found => found is not null);

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