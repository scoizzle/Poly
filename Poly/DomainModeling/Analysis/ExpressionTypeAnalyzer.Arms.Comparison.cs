using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

// Comparison arm: operand compatibility plus enum-member literal validity.

internal sealed partial class ExpressionTypeAnalyzer : INodeAnalyzer {
    private void CheckComparison(
        AnalysisContext context,
        Comparison cmp,
        Dictionary<string, string> props,
        Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        var left = InferType(context, cmp.Left, props, parameters, enumTypes);
        var right = InferType(context, cmp.Right, props, parameters, enumTypes);
        // A6: a bare name against an enum operand is a member of that enum (`Level is High`).
        left = AsEnumMember(cmp.Left, left, right, props, parameters, enumTypes);
        right = AsEnumMember(cmp.Right, right, left, props, parameters, enumTypes);

        // enum member literal validity: Enum prop compared to a string must be a member
        if (Family(left) is TypeFamily.Enum && cmp.Right is Literal { Value: string s })
            CheckEnumMember(context, cmp.Right, left.DomainName!, s, enumTypes);
        if (Family(right) is TypeFamily.Enum && cmp.Left is Literal { Value: string s2 })
            CheckEnumMember(context, cmp.Left, right.DomainName!, s2, enumTypes);

        // C4e (c): if (Level is Nope) — bare identifier on the other side of an enum
        // comparison must be a member of that enum (string literals already checked).
        if (Family(left) is TypeFamily.Enum && cmp.Right is PropertyAccess { Name: var rightName }
            && ResolvePropertyType(rightName, props, parameters) is null)
            CheckEnumMember(context, cmp.Right, left.DomainName!, rightName, enumTypes);
        if (Family(right) is TypeFamily.Enum && cmp.Left is PropertyAccess { Name: var leftName }
            && ResolvePropertyType(leftName, props, parameters) is null)
            CheckEnumMember(context, cmp.Left, right.DomainName!, leftName, enumTypes);

        // Date (DateOnly) and DateTime do not compare. Now is DateTime; Today is Date.
        // Assign still widens Date onto DateTime; this rejection is comparison only.
        // A fractional literal against Number / Int / Int64 is rejected here. Both
        // sides are category Number, so Compatible would allow the pair.
        ReportFractionalLiteral(context, cmp.Left, left, cmp.Right, right);
        if (!IsTyped(left)) RequireTyped(context, cmp.Left, left, cmp, props, parameters, enumTypes);
        if (!IsTyped(right)) RequireTyped(context, cmp.Right, right, cmp, props, parameters, enumTypes);
        // `Flag is true` on an optional Boolean is the lifted comparison: lowering must not fold it to `Flag`.
        if (cmp.Right is Literal { Value: bool })
            StampOptionalBoolean(context, cmp.Left, left);
        // The null rule is for `==` and `!=`: an ordering against `null` is never defined.
        if (MixedDateGranularity(left, right)
            || (IsTyped(left) && IsTyped(right) && !Compatible(left, right) && !NullEqualityInsideHop(cmp, left, right, props))
            || (IsOrdering(cmp.Kind) && (Family(left) is TypeFamily.Null || Family(right) is TypeFamily.Null)))
            Report(context, cmp,
                $"comparison between incompatible types '{Describe(left)}' and '{Describe(right)}'");
        else if (IsOrdering(cmp.Kind) && IsTyped(left) && IsTyped(right)
            && left.Definition is { } leftDefinition && right.Definition is { } rightDefinition
            && leftDefinition.GetOperator(OperatorTable.KindOf(cmp.Kind), rightDefinition) is null)
            Report(context, cmp, $"ordering is not defined for '{Describe(left)}'");

        WalkExpression(context, cmp.Left, props, parameters, enumTypes);
        WalkExpression(context, cmp.Right, props, parameters, enumTypes);
    }

    /// <summary>
    /// Kept because it runs today: inside a hop's condition (<c>line Qty == null</c>, <c>advisor where Ok == null</c>) any
    /// read compares with <c>null</c> by <c>==</c> or <c>!=</c>, and every host answers by the stored value (always false, or
    /// always true, for a required property). The owner may remove it. An ordering against <c>null</c> is not kept.
    /// </summary>
    private static bool NullEqualityInsideHop(Comparison cmp, ExprType left, ExprType right, Dictionary<string, string> props) =>
        props is EntityScope { ThroughHop: true } && !IsOrdering(cmp.Kind) && left.IsNullLiteral != right.IsNullLiteral;

    private static bool IsOrdering(ComparisonKind kind) =>
        kind is ComparisonKind.LessThan or ComparisonKind.LessThanOrEqual
            or ComparisonKind.GreaterThan or ComparisonKind.GreaterThanOrEqual;

    /// <summary>A bare name that is no property or parameter, against an enum that declares it, is that enum's member.</summary>
    private static ExprType AsEnumMember(
        DomainExpression expr, ExprType type, ExprType other,
        Dictionary<string, string> props, Dictionary<string, string>? parameters, Dictionary<string, EnumType> enumTypes) =>
        !type.IsResolved && expr is PropertyAccess { Name: var name }
        && ResolvePropertyType(name, props, parameters) is null
        && Family(other) is TypeFamily.Enum && other.DomainName is { } enumName
        && enumTypes.TryGetValue(enumName, out var declared) && declared.MemberNames.Contains(name, StringComparer.Ordinal)
            ? other
            : type;
}