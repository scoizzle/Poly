using Poly.Analysis;
using Poly.DomainModeling.Effects;

namespace Poly.DomainModeling.Analysis;

public sealed record ActionCapabilityView(
    string ActionName,
    IReadOnlyList<Property> Parameters,
    IReadOnlyList<Effect> Effects,
    IReadOnlyList<Type> EffectTypes,
    IReadOnlyList<Stage> TransitionTargets);

public sealed record StageCapabilityView(
    string StageName,
    IReadOnlyList<ActionCapabilityView> LocalActions,
    IReadOnlyList<ActionCapabilityView> EffectiveActions,
    IReadOnlyList<Policy> LocalPolicies,
    IReadOnlyList<Policy> EffectivePolicies);

public sealed record RelationshipCapabilityView(
    string RelationshipName,
    DomainTypeReference Source,
    DomainTypeReference Target,
    RelationshipCardinality Cardinality,
    IReadOnlyList<Property> Properties,
    IReadOnlyList<Stage> Stages,
    IReadOnlyList<Policy> Policies);

internal sealed record ActionCapabilityMetadata(ActionCapabilityView View) : IAnalysisMetadata;
internal sealed record StageCapabilityMetadata(StageCapabilityView View) : IAnalysisMetadata;
internal sealed record RelationshipCapabilityMetadata(RelationshipCapabilityView View) : IAnalysisMetadata;

internal sealed class CapabilityAnalyzer : INodeAnalyzer {
    public const string Id = "DomainCapabilityAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [];
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        switch (node) {
            case Domain domain:
                AnalyzeDomain(context, domain);
                return;
            case Action action:
                AnalyzeAction(context, action);
                break;
            case Stage stage:
                AnalyzeStage(context, stage);
                break;
            case Relationship relationship:
                AnalyzeRelationship(context, relationship);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        DomainAnalysis.ForEachEntity(domain, entity => {
            foreach (var action in entity.Actions) {
                AnalyzeAction(context, action);
            }
            foreach (var stage in entity.Stages) {
                AnalyzeStage(context, stage);
            }
        });

        foreach (var relationship in domain.Relationships) {
            AnalyzeRelationship(context, relationship);
        }
    }

    private static void AnalyzeAction(AnalysisContext context, Action action) {
        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        var ownerEntity = FindOwnerEntity(lookup, action);

        var transitionTargetStages = new List<Stage>();
        foreach (var effect in FlattenEffects(action.Effects)) {
            if (effect is StageTransitionEffect ste) {
                // Prefer real Stage nodes from the owner entity (DAS W2) over stubs.
                Stage? resolved = null;
                if (ownerEntity is not null) {
                    resolved = ownerEntity.Stages.FirstOrDefault(s =>
                        string.Equals(s.Name, ste.TargetStage.StageName, StringComparison.Ordinal));
                }
                transitionTargetStages.Add(resolved
                    ?? new Stage(ste.TargetStage.StageName, [], [], [], []));
            }
        }

        var view = new ActionCapabilityView(
            ActionName: action.Name,
            Parameters: action.Parameters,
            Effects: action.Effects,
            EffectTypes: action.Effects.Select(static e => e.GetType()).Distinct().ToArray(),
            TransitionTargets: transitionTargetStages);

        context.SetMetadata(action, new ActionCapabilityMetadata(view));
    }

    private static Entity? FindOwnerEntity(DomainTypeLookupMetadata? lookup, Action action) {
        if (lookup is null) return null;
        foreach (var entity in lookup.Entities) {
            if (entity.Actions.Contains(action)) return entity;
            foreach (var stage in entity.Stages) {
                if (stage.Actions.Contains(action)) return entity;
            }
        }
        return null;
    }

    private static void AnalyzeStage(AnalysisContext context, Stage stage) {
        var localActionViews = stage.Actions
            .Select(a => context.GetMetadata<ActionCapabilityMetadata>(a)?.View)
            .OfType<ActionCapabilityView>()
            .ToArray();

        var effectivePolicies = context.GetMetadata<EffectivePoliciesMetadata>(stage)?.Policies ?? [];

        var effectiveActionViews = new List<ActionCapabilityView>();
        effectiveActionViews.AddRange(localActionViews);

        // Stage hierarchy not supported — no parent walk.
        // All effective actions come only from the stage itself.

        var view = new StageCapabilityView(
            StageName: stage.Name,
            LocalActions: localActionViews,
            EffectiveActions: effectiveActionViews,
            LocalPolicies: stage.Policies,
            EffectivePolicies: effectivePolicies);

        context.SetMetadata(stage, new StageCapabilityMetadata(view));
    }

    private static void AnalyzeRelationship(AnalysisContext context, Relationship relationship) {
        var view = new RelationshipCapabilityView(
            RelationshipName: relationship.Name,
            Source: relationship.Source,
            Target: relationship.Target,
            Cardinality: relationship.Cardinality,
            Properties: relationship.Properties,
            Stages: relationship.Stages,
            Policies: relationship.Policies);

        context.SetMetadata(relationship, new RelationshipCapabilityMetadata(view));
    }

    private static IEnumerable<Effect> FlattenEffects(IEnumerable<Effect> effects) =>
        EffectHelpers.FlattenEffects(effects);
}