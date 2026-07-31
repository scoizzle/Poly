using Poly.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Single domain-scoped catalog of name→member maps for semantic lookup.
/// Sole write site for action-resolution and mutation-target indexes (DAS W1.4).
/// Embeds intermediate Semantic type/relationship lookups; product consumers
/// read this bag via <see cref="DomainSemanticLookupExtensions"/>.
/// </summary>
internal sealed record DomainCatalogMetadata(
    Domain Domain,
    DomainTypeLookupMetadata Types,
    RelationshipLookupMetadata Relationships,
    MutationTargetIndexMetadata Index,
    IReadOnlyDictionary<string, ActionResolutionMetadata> ActionsByEntityName
) : IAnalysisMetadata;