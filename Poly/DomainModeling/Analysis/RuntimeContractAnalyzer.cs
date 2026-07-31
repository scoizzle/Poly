using Poly.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Publishes runtime-oriented contracts that are not the domain name→member
/// catalog: relationship contracts and stage-keyed subscription dispatch plans.
/// Action/policy/type indexes are owned by <see cref="DomainCatalogPass"/> (DAS W1.4).
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