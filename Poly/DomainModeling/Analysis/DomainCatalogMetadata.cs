using Poly.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Name→member catalog published by <see cref="DomainCatalogPass"/>.
/// Product lookups go through <see cref="DomainSemanticLookupExtensions"/>.
/// </summary>
internal sealed record DomainCatalogMetadata(
    Domain Domain,
    DomainTypeLookupMetadata Types,
    RelationshipLookupMetadata Relationships,
    MutationTargetIndexMetadata Index,
    IReadOnlyDictionary<string, ActionResolutionMetadata> ActionsByEntityName
) : IAnalysisMetadata;