using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// One owner navigation: <c>SourceEntity.NavName</c> owns <c>TargetEntity</c> (S-12).
/// </summary>
public sealed record OwnedEdge(string SourceEntity, string NavName, string TargetEntity, RelationshipCardinality Cardinality);

/// <summary>
/// Stores ownership hierarchy metadata: which entities own which, and the owner
/// navigations themselves. Produced by <see cref="OwnershipAggregatePass"/>.
/// </summary>
/// <param name="Aggregate">The cross-entity ownership hierarchy.</param>
/// <param name="OwnedEdges">Every owner navigation in declaration order.</param>
public sealed record OwnershipAggregateMetadata(AggregateModel Aggregate, IReadOnlyList<OwnedEdge> OwnedEdges) : IAnalysisMetadata {
    /// <summary>The owner navigations whose target is <paramref name="targetEntity"/>.</summary>
    public IReadOnlyList<OwnedEdge> OwnerEdgesOf(string targetEntity) =>
        OwnedEdges.Where(e => string.Equals(e.TargetEntity, targetEntity, StringComparison.Ordinal)).ToList();
}