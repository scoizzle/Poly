using Poly.DomainModeling.Ontology;

using Prim = Poly.Introspection.PrimitiveType;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Analysis;

// Temporal arms (T-10a): a date offset by a count or a duration, the optional-date refusal, and the site stamp a
// pack-owned bare duration reads. Every shape here either means the same on the simulator, the generated C# and
// `polyc check`, or is a positioned error.

internal sealed partial class ExpressionTypeAnalyzer : INodeAnalyzer {
    /// <summary>Where an expression sits (assign value, create initializer, comparison operand); keyed per node, so equal records never share one.</summary>
    private sealed record CheckSiteMetadata(CheckSite Site, string? TypeName) : IAnalysisMetadata;

    private static void StampSite(AnalysisContext context, DomainExpression value, CheckSite site, string? typeName) {
        if (typeName is not null)
            context.SetMetadata(value, new CheckSiteMetadata(site, typeName));
    }

    /// <summary>A comparison operand is stamped with the other operand's type; only a leaf can be a pack-owned literal.</summary>
    private void StampComparisonSites(
        AnalysisContext context, Comparison cmp,
        Dictionary<string, string> props, Dictionary<string, string>? parameters, Dictionary<string, EnumType> enumTypes) {
        if (IsLeaf(cmp.Left))
            StampOperand(context, cmp.Left, cmp.Right, props, parameters, enumTypes);
        if (IsLeaf(cmp.Right))
            StampOperand(context, cmp.Right, cmp.Left, props, parameters, enumTypes);
    }

    /// <summary>An operand compared with an optional property is stamped <see cref="CheckSite.OptionalOperand"/> (#484: no door compares an optional).</summary>
    private void StampOperand(
        AnalysisContext context, DomainExpression operand, DomainExpression other,
        Dictionary<string, string> props, Dictionary<string, string>? parameters, Dictionary<string, EnumType> enumTypes) {
        var otherType = InferType(context, other, props, parameters, enumTypes);
        StampSite(context, operand,
            OptionalOperand(context, other, otherType, props, parameters) ? CheckSite.OptionalOperand : CheckSite.Operand,
            SiteTypeName(otherType));
    }

    /// <summary>
    /// The one optionality question of the temporal rules. A direct read is optional as its inferred type says
    /// (<see cref="ExprType.Optional"/>). A path or binder read (<c>part Gap</c>, <c>line Due</c>) is optional only when the
    /// member it reaches is DECLARED optional: through a to-one link <c>Optional</c> also marks a required member so that
    /// <c>== null</c> stays accepted, and such a read cannot be absent at run time.
    /// </summary>
    private static bool OptionalOperand(
        AnalysisContext context, DomainExpression expr, ExprType type,
        Dictionary<string, string> props, Dictionary<string, string>? parameters) {
        if (expr is not RelationshipNavigation navigation)
            return type.Optional;
        var target = NavigationRoot(context, navigation.RelationshipName, props, parameters);
        var hop = navigation.TargetProperty;
        while (EntityNamed(context, target) is { } current) {
            switch (hop) {
                case PropertyAccess leaf:
                    return current.Properties.FirstOrDefault(p => string.Equals(p.Name, leaf.Name, StringComparison.Ordinal))
                        is { } declared && !EntityStructureAnalyzer.IsRequired(declared);
                case RelationshipNavigation next:
                    target = ToOneTarget(current, next.RelationshipName);
                    hop = next.TargetProperty;
                    continue;
                default:
                    return false;
            }
        }
        return false;
    }

    private static bool IsLeaf(DomainExpression expr) => !expr.Children.OfType<DomainExpression>().Any();

    private static string? SiteTypeName(ExprType type) => IsTyped(type) ? type.DomainName ?? Describe(type) : null;

    private ExpressionTypeCheckScope CheckScope(
        AnalysisContext context, DomainExpression expr,
        Dictionary<string, string> props, Dictionary<string, string>? parameters, Dictionary<string, EnumType> enumTypes) {
        var stamp = context.GetMetadata<CheckSiteMetadata>(expr);
        return new ExpressionTypeCheckScope(
            props, parameters, enumTypes,
            Site: stamp?.Site ?? CheckSite.Default,
            SiteTypeName: stamp?.TypeName,
            IsOptional: name => IsOptionalProperty(name, props));
    }

    /// <summary>
    /// A date plus or minus a number or a duration. Returns true when the temporal rules own the shape, accepted or
    /// refused; false leaves it to the arithmetic rule's "not numeric" message.
    /// </summary>
    private bool CheckTemporalOffset(
        AnalysisContext context, DomainExpression node, DomainExpression left, ExprType leftType,
        ExprType rightType, Dictionary<string, string> props, Dictionary<string, string>? parameters) {
        if (!IsDate(leftType))
            return false;
        if (Family(rightType) is TypeFamily.Duration) {
            Report(context, node,
                "a date plus or minus a Duration value is not supported yet (F-11, #550); " +
                "offset by a literal such as 'Due + 3 Days' or by a whole Number count");
            return true;
        }
        if (!IsNumeric(rightType))
            return false;
        if (OptionalOperand(context, left, leftType, props, parameters))
            Report(context, node, OptionalDateMessage(left));
        else if (IsRefusedCount(rightType))
            Report(context, node,
                $"a Decimal day count is refused (got '{PrintedText(context, node)}'): " +
                "use a whole Number count or a unit literal such as '36 Hours'");
        else if (left is not PropertyAccess)
            Report(context, node,
                $"a date offset by a count is supported only on a date property or parameter (got '{PrintedText(context, node)}'); " +
                "use a literal such as '3 Days' for anything else");
        return true;
    }

    /// <summary>
    /// R4 (owner ruling 2026-11-13): every Decimal day count is refused. One predicate: a Decimal count dropped its
    /// fraction on a Date and failed on a host everywhere else.
    /// </summary>
    private static bool IsRefusedCount(ExprType countType) => countType.Definition?.PrimitiveType is Prim.Decimal;

    /// <summary>R3 (#648): the operands of a date difference may not be optional; neither door computes with an unset date.</summary>
    private void CheckDateDifferenceOperands(
        AnalysisContext context, Subtract difference, ExprType leftType, ExprType rightType,
        Dictionary<string, string> props, Dictionary<string, string>? parameters) {
        if (OptionalOperand(context, difference.Left, leftType, props, parameters))
            Report(context, difference, OptionalDateMessage(difference.Left));
        if (OptionalOperand(context, difference.Right, rightType, props, parameters))
            Report(context, difference, OptionalDateMessage(difference.Right));
    }

    private static string OptionalDateMessage(DomainExpression operand) =>
        $"an offset or difference on the optional date '{(operand as PropertyAccess)?.Name}' is not supported (#648): " +
        "the property may be unset and no door computes with an absent date";
}