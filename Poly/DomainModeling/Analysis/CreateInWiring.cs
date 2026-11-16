using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// The one rule for which navigation of the created entity a <c>create in rel</c> fills with the
/// source (<c>this</c>). Analysis reads it for DMEFF011's exemption; it is published on
/// <see cref="RelationshipContract.CreateInFilledNavigation"/>, which lowering reads, so the
/// exemption and the generated <c>Create{Nav}</c> never disagree.
/// </summary>
internal static class CreateInWiring {
    /// <summary>
    /// The singular navigation on <paramref name="target"/> that <c>create in</c> over
    /// <paramref name="relationship"/> (declared on <paramref name="source"/>) fills with the source:
    /// the navigation the relationship's <c>via</c> names, else the one whose <c>via</c> names the
    /// relationship, else the only singular navigation on the target that points back to the source.
    /// Null when none qualifies or it is ambiguous. A self-relationship fills its peer only for a
    /// collection create-in (the new child's parent is <c>this</c>).
    /// </summary>
    public static Relationship? FilledNavigation(Entity source, Relationship relationship, Entity target) {
        var self = string.Equals(source.Name, target.Name, StringComparison.Ordinal);
        if (self && !IsCollection(relationship))
            return null;

        if (relationship.Inverse is { Length: > 0 } declared) {
            var named = target.Navigations.FirstOrDefault(n => string.Equals(n.Name, declared, StringComparison.Ordinal));
            if (named is not null && PointsBack(named, source))
                return named;
        }

        foreach (var nav in target.Navigations) {
            if (PointsBack(nav, source) && string.Equals(nav.Inverse, relationship.Name, StringComparison.Ordinal))
                return nav;
        }

        Relationship? found = null;
        var count = 0;
        foreach (var nav in target.Navigations) {
            if (PointsBack(nav, source)) {
                found = nav;
                count++;
            }
        }
        return count == 1 ? found : null;
    }

    private static bool PointsBack(Relationship nav, Entity source) =>
        !IsCollection(nav) && string.Equals(nav.Target.TypeName, source.Name, StringComparison.Ordinal);

    private static bool IsCollection(Relationship nav) =>
        nav.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;

    /// <summary>
    /// The published fact for <c>create in</c> over <paramref name="relationshipName"/> on
    /// <paramref name="sourceEntityName"/>: the filled navigation's name, or null. A missing
    /// contract bag is a lowering fault: it throws rather than recomputing the rule.
    /// </summary>
    public static string? FilledNavigationName(
        INodeMetadataProvider analysis, string sourceEntityName, string relationshipName) {
        var contracts = analysis.GetMetadata<RelationshipContractMetadata>(default)
            ?? throw new InvalidOperationException(
                $"{nameof(RelationshipContractMetadata)} is required to lower create in '{sourceEntityName}.{relationshipName}'.");
        var contract = contracts.Contracts.FirstOrDefault(c =>
                string.Equals(c.SourceEntityName, sourceEntityName, StringComparison.Ordinal)
                && string.Equals(c.Name, relationshipName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"No relationship contract for '{sourceEntityName}.{relationshipName}'.");
        return contract.CreateInFilledNavigation;
    }
}