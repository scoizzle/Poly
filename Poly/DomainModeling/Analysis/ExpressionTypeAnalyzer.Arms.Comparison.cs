using Poly.DomainModeling.Ontology;

using Add = Poly.DomainModeling.Ontology.Add;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

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
        if (MixedDateGranularity(left, right) || (!NullAgainstRead(context, cmp, props) && !Compatible(left, right)))
            Report(context, cmp,
                $"comparison between incompatible types '{Describe(left)}' and '{Describe(right)}'");

        WalkExpression(context, cmp.Left, props, parameters, enumTypes);
        WalkExpression(context, cmp.Right, props, parameters, enumTypes);
    }

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

    /// <summary>
    /// Seam N2 (T-05a review F2 and closing round; removing row T-06): <c>null</c> compared with an expression whose
    /// type carries no optionality yet keeps master's answer, accepted, so T-06 does not flip it back: a path-prefix,
    /// binder or owned read (an optional member, an unlinked hop), a <c>match</c> with a <c>null</c> arm, and
    /// arithmetic over an optional property. Arithmetic over required operands meets the comparison rule.
    /// </summary>
    private static bool NullAgainstRead(AnalysisContext context, Comparison cmp, Dictionary<string, string> props) =>
        cmp.Left is Literal { Value: null } && MayBeAbsent(context, cmp.Right, props)
        || cmp.Right is Literal { Value: null } && MayBeAbsent(context, cmp.Left, props);

    private static bool MayBeAbsent(AnalysisContext context, DomainExpression expr, Dictionary<string, string> props) =>
        expr switch {
            RelationshipNavigation or OwnedAccess => true,
            MatchExpr match => match.Arms.Any(a => a.Value is Literal { Value: null }) || match.Else is Literal { Value: null },
            Add or Subtract or Multiply or Divide => expr.Children.OfType<DomainExpression>()
                .Any(operand => IsOptionalProperty(context, operand, props)
                    || operand is Add or Subtract or Multiply or Divide && MayBeAbsent(context, operand, props)),
            _ => false,
        };

    private static bool IsOptionalProperty(AnalysisContext context, DomainExpression expr, Dictionary<string, string> props) =>
        expr is PropertyAccess property
        && props is EntityScope scope
        && EntityNamed(context, scope.EntityName)?.Properties
            .FirstOrDefault(p => string.Equals(p.Name, property.Name, StringComparison.Ordinal)) is { } declared
        && !EntityStructureAnalyzer.IsRequired(declared);
}