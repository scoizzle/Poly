using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

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
        if (!context.TryBeginAnalyzerVisit<CapabilityAnalyzer>(domain)) {
            return;
        }

        foreach (var type in domain.Types) {
            if (type is Entity entity) {
                foreach (var action in entity.Actions) {
                    AnalyzeAction(context, action);
                }
                foreach (var stage in entity.Stages) {
                    AnalyzeStage(context, stage);
                }
            }
        }

        foreach (var relationship in domain.Relationships) {
            AnalyzeRelationship(context, relationship);
        }
    }

    private static void AnalyzeAction(AnalysisContext context, Action action) {
        if (!context.TryBeginAnalyzerVisit<CapabilityAnalyzer>(action)) {
            return;
        }

        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);

        var transitionTargetStages = new List<Stage>();
        foreach (var effect in FlattenEffects(action.Effects)) {
            if (effect is StageTransitionEffect ste) {
                transitionTargetStages.Add(new Stage(ste.TargetStage.StageName, [], [], [], []));
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

    private static void AnalyzeStage(AnalysisContext context, Stage stage) {
        if (!context.TryBeginAnalyzerVisit<CapabilityAnalyzer>(stage)) {
            return;
        }

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
        if (!context.TryBeginAnalyzerVisit<CapabilityAnalyzer>(relationship)) {
            return;
        }

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