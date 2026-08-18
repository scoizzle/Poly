using Poly.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using PrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;
using Subtract = Poly.DomainModeling.Ontology.Subtract;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Publishes runtime-oriented contracts that are not the domain name→member
/// catalog: relationship contracts and subscription dispatch plans
/// (stage-scoped on <see cref="Stage"/>; entity-level always-active on <see cref="Entity"/>).
/// Action/policy/type indexes are owned by <see cref="DomainCatalogPass"/>.
/// </summary>
/// <remarks>
/// <para>
/// Entity-level design: entity-level <c>Entity.Subscriptions</c> use the same
/// <see cref="SubscriptionDispatchPlanMetadata"/> / <see cref="SubscriptionDispatchPlanEntry"/>
/// shape as stage plans, bagged on the <see cref="Entity"/> node (not a domain-scoped map).
/// Runtime notify (entity-level dispatch order) consults the entity bag in addition to the current stage plan;
/// this pass only publishes facts.
/// </para>
/// </remarks>
internal sealed class RuntimeContractAnalyzer : INodeAnalyzer {
    public const string Id = "DomainRuntimeContractAnalyzer";

    public string PassName => Id;

    public string[] Dependencies => [DomainCatalogPass.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        switch (node) {
            case Domain domain:
                PublishRelationshipContracts(context, domain);
                break;
            case Entity entity:
                PublishEntitySubscriptionDispatchPlan(context, entity);
                break;
            case Stage stage:
                PublishSubscriptionDispatchPlan(context, stage);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void PublishRelationshipContracts(AnalysisContext context, Domain domain) {
        var contracts = context.GetAllRelationships(domain)
            .Select(r => new RelationshipContract(
                Name: r.Name,
                SourceEntityName: r.Source.TypeName,
                TargetEntityName: r.Target.TypeName,
                Cardinality: r.Cardinality,
                SourceOwnsTarget: r.SourceOwnsTarget))
            .ToList();

        context.SetMetadata(default, new RelationshipContractMetadata(contracts));
    }

    private static void PublishEntitySubscriptionDispatchPlan(AnalysisContext context, Entity entity) {
        if (entity.Subscriptions.Count == 0) {
            context.SetMetadata(entity,
                new SubscriptionDispatchPlanMetadata(
                    new Dictionary<string, IReadOnlyList<SubscriptionDispatchPlanEntry>>(StringComparer.Ordinal)));
            return;
        }

        if (context.GetMetadata<RelationshipContractMetadata>(default) is not RelationshipContractMetadata relationshipContracts) {
            return;
        }

        var entries = BuildSubscriptionEntries(
            context,
            relationshipContracts,
            entity.Name,
            entity.Subscriptions,
            scopeLabel: $"entity '{entity.Name}'",
            reportOn: entity);

        var byRelationship = entries
            .GroupBy(e => e.RelationshipName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<SubscriptionDispatchPlanEntry>)g.ToList(), StringComparer.Ordinal);

        context.SetMetadata(entity, new SubscriptionDispatchPlanMetadata(byRelationship));
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

        var entries = BuildSubscriptionEntries(
            context,
            relationshipContracts,
            sourceEntity.Name,
            stage.Subscriptions,
            scopeLabel: $"stage '{stage.Name}'",
            reportOn: stage);

        var byRelationship = entries
            .GroupBy(e => e.RelationshipName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<SubscriptionDispatchPlanEntry>)g.ToList(), StringComparer.Ordinal);

        context.SetMetadata(stage, new SubscriptionDispatchPlanMetadata(byRelationship));
    }

    /// <summary>
    /// Shared contract resolution for stage- and entity-scoped subscription plans.
    /// Fails closed (structural) when the relationship name is not unique for the source entity.
    /// </summary>
    private static List<SubscriptionDispatchPlanEntry> BuildSubscriptionEntries(
        AnalysisContext context,
        RelationshipContractMetadata relationshipContracts,
        string sourceEntityName,
        IReadOnlyList<StageSubscription> subscriptions,
        string scopeLabel,
        Node reportOn) {
        var entries = new List<SubscriptionDispatchPlanEntry>();
        foreach (var subscription in subscriptions) {
            var contracts = relationshipContracts.Contracts.Where(c =>
                string.Equals(c.Name, subscription.RelationshipName, StringComparison.Ordinal)
                && string.Equals(c.SourceEntityName, sourceEntityName, StringComparison.Ordinal)).ToList();

            if (contracts.Count != 1) {
                context.ReportStructuralFailure(reportOn,
                    $"Subscription relationship '{subscription.RelationshipName}' on {scopeLabel} could not be uniquely resolved for source entity '{sourceEntityName}'.",
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
                Effects: subscription.Effects,
                PeerBinding: subscription.PeerBinding));
        }

        return entries;
    }

    private static Entity? FindOwnerEntity(AnalysisContext context, Stage stage) =>
        context.GetMetadata<OwnerEntityMetadata>(stage)?.Owner;
}