using Poly.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Publishes <see cref="DomainCatalogMetadata"/> — one domain-keyed catalog
/// composing type/relationship indexes, mutation target index, and per-entity
/// action-resolution maps. Does not re-walk the tree for facts already published.
/// </summary>
internal sealed class DomainCatalogPass : INodeAnalyzer {
    public const string Id = "DomainCatalogPass";
    public string PassName => Id;
    public string[] Dependencies => [SemanticDomainAnalyzer.Id, RuntimeContractAnalyzer.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain) return;
        if (context.HasStructuralFailure) return;
        if (!context.ShouldAnalyze(node)) return;

        var types = context.GetMetadata<DomainTypeLookupMetadata>(default);
        var relationships = context.GetMetadata<RelationshipLookupMetadata>(default);
        var index = context.GetMetadata<MutationTargetIndexMetadata>(domain);
        if (types is null || relationships is null || index is null)
            return;

        var actionsByEntity = new Dictionary<string, ActionResolutionMetadata>(StringComparer.Ordinal);
        foreach (var entity in types.Entities) {
            var arm = context.GetMetadata<ActionResolutionMetadata>(entity);
            if (arm is not null)
                actionsByEntity[entity.Name] = arm;
        }

        context.SetMetadata(domain, new DomainCatalogMetadata(
            Domain: domain,
            Types: types,
            Relationships: relationships,
            Index: index,
            ActionsByEntityName: actionsByEntity));
    }
}