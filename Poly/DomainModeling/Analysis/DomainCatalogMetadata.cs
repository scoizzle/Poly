using Poly.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Single domain-scoped catalog of name→member maps for semantic lookup.
/// Published after semantic + runtime-contract indexes exist (DAS W1).
/// Consumers should prefer this bag over reading MTI/DTLM/RLM/ARM separately.
/// </summary>
internal sealed record DomainCatalogMetadata(
    Domain Domain,
    DomainTypeLookupMetadata Types,
    RelationshipLookupMetadata Relationships,
    MutationTargetIndexMetadata Index,
    IReadOnlyDictionary<string, ActionResolutionMetadata> ActionsByEntityName
) : IAnalysisMetadata;