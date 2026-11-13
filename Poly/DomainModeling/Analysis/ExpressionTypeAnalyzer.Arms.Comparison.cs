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
        if (MixedDateGranularity(left, right) || (!NullAgainstRead(cmp) && !Compatible(left, right)))
            Report(context, cmp,
                $"comparison between incompatible types '{Describe(left)}' and '{Describe(right)}'");

        WalkExpression(context, cmp.Left, props, parameters, enumTypes);
        WalkExpression(context, cmp.Right, props, parameters, enumTypes);
    }

    /// <summary>
    /// Seam N2 (T-05a review F2; owner T-06): <c>null</c> compared with a path-prefix, binder or owned read keeps
    /// master's answer, accepted. That read's type carries no optionality yet (an optional member, an unlinked hop),
    /// so rejecting it here would flip back once T-06 types optionality.
    /// </summary>
    private static bool NullAgainstRead(Comparison cmp) =>
        cmp.Left is Literal { Value: null } && cmp.Right is RelationshipNavigation or OwnedAccess
        || cmp.Right is Literal { Value: null } && cmp.Left is RelationshipNavigation or OwnedAccess;
}