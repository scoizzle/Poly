using Poly.DomainModeling.Ontology;

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

    /// <summary>A comparison operand is stamped with the other operand's type.</summary>
    private void StampOperand(
        AnalysisContext context, DomainExpression operand, DomainExpression other,
        Dictionary<string, string> props, Dictionary<string, string>? parameters, Dictionary<string, EnumType> enumTypes) =>
        StampSite(context, operand, CheckSite.Operand, SiteTypeName(InferType(context, other, props, parameters, enumTypes)));

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
        DomainExpression right, ExprType rightType,
        Dictionary<string, string> props, Dictionary<string, string>? parameters) {
        if (!IsDate(leftType))
            return false;
        if (Family(rightType) is TypeFamily.Duration) {
            // #654: DateTime has a TimeSpan addition, so the value lowers to DateTime.Add/Subtract and every host
            // runs it. Every shape the lowering cannot name fails closed here naming the operand instead.
            if (OptionalOperand(context, left, leftType, props, parameters)) {
                Report(context, node, OptionalDateMessage(left));
                return true;
            }
            if (!IsDurationOffsetOperand(left)) {
                Report(context, node,
                    "a date offset by a Duration value is supported only on a date property, a parameter or Now " +
                    $"(got '{PrintedText(context, node)}')");
                return true;
            }
            if (IsDateOnly(leftType)) {
                Report(context, node,
                    $"a Date plus or minus a Duration value is not supported (got '{PrintedText(context, node)}'): " +
                    "a Date has no TimeSpan addition; use a DateTime property or a whole Number day count");
                return true;
            }
            if (OptionalOperand(context, right, rightType, props, parameters)) {
                Report(context, node,
                    $"an offset by the optional Duration '{OperandName(context, right)}' is not supported: " +
                    "an absent Duration has no value to add");
                return true;
            }
            if (!IsDurationOffsetOperand(right)) {
                Report(context, node,
                    "a Duration offset operand must be a Duration property, a parameter or a date difference " +
                    $"(got '{PrintedText(context, right)}')");
            }
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
    private static bool IsRefusedCount(ExprType countType) => DomainTypeSystem.WordOf(countType.Definition) is "Decimal";

    /// <summary>
    /// The operands the Duration lowering names (<c>DateTime.Add(TimeSpan)</c>): a property, a parameter, a clock node,
    /// a date difference or a nested offset. A path through a relationship, an owned member, a match or a call has no
    /// lowering the temporal handlers can reach, so it fails closed here rather than at a host (#654).
    /// </summary>
    private static bool IsDurationOffsetOperand(DomainExpression operand) =>
        operand is not (RelationshipNavigation or OwnedAccess or MatchExpr or FunctionCall);

    /// <summary>The operand's name for a refusal message.</summary>
    private static string OperandName(AnalysisContext context, DomainExpression operand) => operand switch {
        PropertyAccess pa => pa.Name,
        ParameterAccess pa => pa.Name,
        _ => PrintedText(context, operand),
    };

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