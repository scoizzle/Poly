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
            StampSite(context, cmp.Left, CheckSite.Operand, SiteTypeName(InferType(context, cmp.Right, props, parameters, enumTypes)));
        if (IsLeaf(cmp.Right))
            StampSite(context, cmp.Right, CheckSite.Operand, SiteTypeName(InferType(context, cmp.Left, props, parameters, enumTypes)));
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
            IsOptional: name => IsOptionalProperty(context, DomainExpression.Property(name), props));
    }

    /// <summary>
    /// A date plus or minus a number or a duration. Returns true when the temporal rules own the shape, accepted or
    /// refused; false leaves it to the arithmetic rule's "not numeric" message.
    /// </summary>
    private bool CheckTemporalOffset(
        AnalysisContext context, DomainExpression node, DomainExpression left, ExprType leftType,
        ExprType rightType, Dictionary<string, string> props) {
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
        if (IsOptionalProperty(context, left, props))
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
        AnalysisContext context, Subtract difference, Dictionary<string, string> props) {
        if (IsOptionalProperty(context, difference.Left, props))
            Report(context, difference, OptionalDateMessage(difference.Left));
        if (IsOptionalProperty(context, difference.Right, props))
            Report(context, difference, OptionalDateMessage(difference.Right));
    }

    private static string OptionalDateMessage(DomainExpression operand) =>
        $"an offset or difference on the optional date '{(operand as PropertyAccess)?.Name}' is not supported (#648): " +
        "the property may be unset and no door computes with an absent date";
}