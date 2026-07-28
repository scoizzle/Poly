using Poly.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Publishes runtime-oriented semantic contracts so dynamic runtime paths
/// can avoid re-deriving static meaning from direct domain tree scans.
/// </summary>
internal sealed class RuntimeContractAnalyzer : INodeAnalyzer {
    public const string Id = "DomainRuntimeContractAnalyzer";

    public string PassName => Id;

    public string[] Dependencies => [SemanticDomainAnalyzer.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        switch (node) {
            case Domain domain:
                PublishRelationshipContracts(context, domain);
                PublishMutationTargetIndex(context, domain);
                break;
            case Entity entity:
                PublishActionResolution(context, entity);
                break;
            case Stage stage:
                PublishSubscriptionDispatchPlan(context, stage);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void PublishRelationshipContracts(AnalysisContext context, Domain domain) {
        var contracts = domain.Relationships
            .Select(r => new RelationshipContract(
                Name: r.Name,
                SourceEntityName: r.Source.TypeName,
                TargetEntityName: r.Target.TypeName,
                Cardinality: r.Cardinality,
                SourceOwnsTarget: r.SourceOwnsTarget))
            .ToList();

        context.SetMetadata(default, new RelationshipContractMetadata(contracts));
    }

    private static void PublishActionResolution(AnalysisContext context, Entity entity) {
        var entityActions = entity.Actions
            .GroupBy(a => a.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

        var stageByName = entity.Stages
            .GroupBy(s => s.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

        var stageActions = new Dictionary<string, IReadOnlyDictionary<string, Action>>(StringComparer.Ordinal);
        foreach (var stage in entity.Stages) {
            var actions = stage.Actions
                .GroupBy(a => a.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
            stageActions[stage.Name] = actions;
        }

        context.SetMetadata(entity, new ActionResolutionMetadata(entityActions, stageActions, stageByName));
    }

    private static void PublishMutationTargetIndex(AnalysisContext context, Domain domain) {
        var typesByName = domain.Types
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

        var entitiesByName = domain.Types.OfType<Entity>()
            .GroupBy(e => e.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

        var relationshipsByName = domain.Relationships
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

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

        context.SetMetadata(domain, new MutationTargetIndexMetadata(
            TypesByName: typesByName,
            EntitiesByName: entitiesByName,
            RelationshipsByName: relationshipsByName,
            StagesByEntity: stagesByEntity,
            ActionsByEntity: actionsByEntity,
            EntityPoliciesByEntity: entityPoliciesByEntity,
            StagePoliciesByEntity: stagePoliciesByEntity,
            ActionPoliciesByEntity: actionPoliciesByEntity));
    }

    private static void PublishSubscriptionDispatchPlan(AnalysisContext context, Stage stage) {
        if (stage.Subscriptions.Count == 0) {
            context.SetMetadata(stage,
                new SubscriptionDispatchPlanMetadata(
                    new Dictionary<string, IReadOnlyList<SubscriptionDispatchPlanEntry>>(StringComparer.Ordinal)));
            return;
        }

        if (context.GetMetadata<RelationshipContractMetadata>(default) is not RelationshipContractMetadata relationshipContracts) {
            return;
        }

        var sourceEntity = FindOwnerEntity(context, stage);
        if (sourceEntity is null) {
            return;
        }

        var entries = new List<SubscriptionDispatchPlanEntry>();
        foreach (var subscription in stage.Subscriptions) {
            var contracts = relationshipContracts.Contracts.Where(c =>
                string.Equals(c.Name, subscription.RelationshipName, StringComparison.Ordinal)
                && string.Equals(c.SourceEntityName, sourceEntity.Name, StringComparison.Ordinal)).ToList();

            if (contracts.Count != 1) {
                context.ReportStructuralFailure(stage,
                    $"Subscription relationship '{subscription.RelationshipName}' on stage '{stage.Name}' could not be uniquely resolved for source entity '{sourceEntity.Name}'.",
                    DomainModelDiagnosticCodes.SemanticReferenceResolution);
                continue;
            }

            var contract = contracts[0];
            var stageNames = new HashSet<string>(subscription.StageNames, StringComparer.Ordinal);
            entries.Add(new SubscriptionDispatchPlanEntry(
                RelationshipName: contract.Name,
                SourceEntityName: contract.SourceEntityName,
                TargetEntityName: contract.TargetEntityName,
                Quantifier: subscription.Quantifier,
                StageNames: stageNames,
                Effects: subscription.Effects));
        }

        var byRelationship = entries
            .GroupBy(e => e.RelationshipName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<SubscriptionDispatchPlanEntry>)g.ToList(), StringComparer.Ordinal);

        context.SetMetadata(stage, new SubscriptionDispatchPlanMetadata(byRelationship));
    }

    private static Entity? FindOwnerEntity(AnalysisContext context, Stage stage) {
        if (context.GetMetadata<DomainTypeLookupMetadata>(default) is not DomainTypeLookupMetadata lookup) {
            return null;
        }

        return lookup.Entities.FirstOrDefault(entity => entity.Stages.Contains(stage));
    }
}