using Poly.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Sole publisher of the domain-scoped name→member catalog.
/// Builds action-resolution maps and the mutation-target index once, embeds
/// Semantic DTLM/RLM, and attaches only <see cref="DomainCatalogMetadata"/>
/// (no separate entity ARM or domain MTI dual-write).
/// </summary>
internal sealed class DomainCatalogPass : INodeAnalyzer {
    public const string Id = "DomainCatalogPass";
    public string PassName => Id;
    // Reads DomainTypeLookupMetadata + RelationshipLookupMetadata from Semantic only.
    public string[] Dependencies => [SemanticDomainAnalyzer.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain) return;
        if (!context.ShouldAnalyze(node)) return;

        var types = context.GetMetadata<DomainTypeLookupMetadata>(default);
        var relationships = context.GetMetadata<RelationshipLookupMetadata>(default);
        if (types is null || relationships is null) {
            // Fail closed: catalog is required for product semantic consumers.
            context.ReportStructuralFailure(
                domain,
                "Domain catalog requires DomainTypeLookupMetadata and RelationshipLookupMetadata from SemanticDomainAnalyzer.",
                DomainModelDiagnosticCodes.SemanticReferenceResolution);
            return;
        }

        var actionsByEntity = new Dictionary<string, ActionResolutionMetadata>(StringComparer.Ordinal);
        foreach (var entity in types.Entities) {
            actionsByEntity[entity.Name] = BuildActionResolution(entity);
        }

        var index = BuildMutationTargetIndex(domain);

        context.SetMetadata(domain, new DomainCatalogMetadata(
            Domain: domain,
            Types: types,
            Relationships: relationships,
            Index: index,
            ActionsByEntityName: actionsByEntity));
    }

    private static ActionResolutionMetadata BuildActionResolution(Entity entity) {
        var entityActions = entity.Actions
            .GroupBy(a => a.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

        var stageActions = new Dictionary<string, IReadOnlyDictionary<string, Action>>(StringComparer.Ordinal);
        foreach (var stage in entity.Stages) {
            var actions = stage.Actions
                .GroupBy(a => a.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
            stageActions[stage.Name] = actions;
        }

        return new ActionResolutionMetadata(entityActions, stageActions);
    }

    private static MutationTargetIndexMetadata BuildMutationTargetIndex(Domain domain) {
        var typesByName = domain.Types
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

        var entitiesByName = domain.Types.OfType<Entity>()
            .GroupBy(e => e.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

        var relationshipsByName = domain.Types.OfType<Entity>()
            .SelectMany(e => e.Navigations)
            .GroupBy(r => r.Source.TypeName, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, Relationship>)g
                    .GroupBy(r => r.Name, StringComparer.Ordinal)
                    .ToDictionary(x => x.Key, x => x.Last(), StringComparer.Ordinal),
                StringComparer.Ordinal);

        var stagesByEntity = new Dictionary<string, IReadOnlyDictionary<string, Stage>>(StringComparer.Ordinal);
        var actionsByEntity = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Action>>>(StringComparer.Ordinal);
        var entityPoliciesByEntity = new Dictionary<string, IReadOnlyDictionary<string, Policy>>(StringComparer.Ordinal);
        var stagePoliciesByEntity = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, Policy>>>(StringComparer.Ordinal);
        var actionPoliciesByEntity = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, Policy>>>(StringComparer.Ordinal);

        foreach (var entity in entitiesByName.Values) {
            var stageLookup = entity.Stages
                .GroupBy(s => s.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
            stagesByEntity[entity.Name] = stageLookup;

            var actionLookup = new Dictionary<string, IReadOnlyList<Action>>(StringComparer.Ordinal);
            foreach (var action in entity.Actions.GroupBy(a => a.Name, StringComparer.Ordinal)) {
                actionLookup[action.Key] = action.ToList();
            }
            foreach (var stage in entity.Stages) {
                foreach (var action in stage.Actions.GroupBy(a => a.Name, StringComparer.Ordinal)) {
                    if (!actionLookup.TryGetValue(action.Key, out var existing)) {
                        actionLookup[action.Key] = action.ToList();
                        continue;
                    }

                    actionLookup[action.Key] = [.. existing, .. action];
                }
            }
            actionsByEntity[entity.Name] = actionLookup;

            entityPoliciesByEntity[entity.Name] = entity.Policies
                .GroupBy(p => p.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

            var stagePolicyLookup = new Dictionary<string, IReadOnlyDictionary<string, Policy>>(StringComparer.Ordinal);
            var actionPolicyLookup = new Dictionary<string, IReadOnlyDictionary<string, Policy>>(StringComparer.Ordinal);
            foreach (var stage in entity.Stages) {
                stagePolicyLookup[stage.Name] = stage.Policies
                    .GroupBy(p => p.Name, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

                foreach (var action in stage.Actions) {
                    actionPolicyLookup[action.Name] = action.Policies
                        .GroupBy(p => p.Name, StringComparer.Ordinal)
                        .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
                }
            }

            foreach (var action in entity.Actions) {
                actionPolicyLookup[action.Name] = action.Policies
                    .GroupBy(p => p.Name, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
            }

            stagePoliciesByEntity[entity.Name] = stagePolicyLookup;
            actionPoliciesByEntity[entity.Name] = actionPolicyLookup;
        }

        return new MutationTargetIndexMetadata(
            TypesByName: typesByName,
            EntitiesByName: entitiesByName,
            RelationshipsByName: relationshipsByName,
            StagesByEntity: stagesByEntity,
            ActionsByEntity: actionsByEntity,
            EntityPoliciesByEntity: entityPoliciesByEntity,
            StagePoliciesByEntity: stagePoliciesByEntity,
            ActionPoliciesByEntity: actionPoliciesByEntity);
    }
}