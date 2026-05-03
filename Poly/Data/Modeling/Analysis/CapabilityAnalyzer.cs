using Poly.Data.Modeling.Effects;
using Poly.Syntax.Analysis;

namespace Poly.Data.Modeling;

internal sealed record ActionCapabilityMetadata(ActionCapabilityView View) : IAnalysisMetadata;
internal sealed record StageCapabilityMetadata(StageCapabilityView View) : IAnalysisMetadata;
internal sealed record RelationshipCapabilityMetadata(RelationshipCapabilityView View) : IAnalysisMetadata;

internal sealed class CapabilityAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        switch (node) {
            case Domain request:
                AnalyzeDomain(context, request.Domain);
                break;
            case Relationship relationship:
                AnalyzeRelationship(context, relationship);
                AnalyzeEntity(context, relationship);
                break;
            case Entity entity:
                AnalyzeEntity(context, entity);
                break;
            case Action action:
                AnalyzeAction(context, action);
                break;
            case Stage stage:
                AnalyzeStage(context, stage);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        foreach (var entity in domain.Types.OfType<Entity>().Where(context.ShouldAnalyze)) {
            AnalyzeEntity(context, entity);
        }
        foreach (var relationship in domain.Relationships.Where(context.ShouldAnalyze)) {
            AnalyzeRelationship(context, relationship);
        }
    }

    private static void AnalyzeEntity(AnalysisContext context, Entity entity) {
        // Actions must be processed before stages: StageCapabilityMetadata depends on ActionCapabilityMetadata.
        foreach (var action in entity.Actions.Concat(entity.Stages.SelectMany(static s => s.Actions))) {
            if (context.ShouldAnalyze(action)) {
                AnalyzeAction(context, action);
            }
        }
        foreach (var stage in entity.Stages.Where(context.ShouldAnalyze)) {
            AnalyzeStage(context, stage);
        }
    }

    private static void AnalyzeAction(AnalysisContext context, Action action) {
        if (!context.TryBeginAnalyzerVisit<CapabilityAnalyzer>(action)) {
            return;
        }

        var view = new ActionCapabilityView(
            ActionName: action.Name,
            Parameters: action.Parameters.OfType<Property>().ToArray(),
            Effects: action.Effects.ToArray(),
            EffectTypes: action.Effects.Select(static e => e.GetType()).Distinct().ToArray(),
            PublishedEvents: action.Effects.OfType<PublishEvent>().Select(static e => e.Event).ToArray(),
            TransitionTargets: action.Effects.OfType<StageTransition>().Select(static e => e.TargetStage).ToArray());

        context.SetMetadata(action, new ActionCapabilityMetadata(view));
    }

    private static void AnalyzeStage(AnalysisContext context, Stage stage) {
        if (!context.TryBeginAnalyzerVisit<CapabilityAnalyzer>(stage)) {
            return;
        }

        var effectiveStage = context.GetMetadata<EffectiveStageMetadata>(stage);
        if (effectiveStage is null) {
            return;
        }

        var localActions = stage.Actions
            .Select(action => context.GetMetadata<ActionCapabilityMetadata>(action)?.View)
            .OfType<ActionCapabilityView>()
            .ToArray();

        var effectiveActions = effectiveStage.EffectiveActions
            .Select(action => context.GetMetadata<ActionCapabilityMetadata>(action)?.View)
            .OfType<ActionCapabilityView>()
            .ToArray();

        var view = new StageCapabilityView(
            StageName: stage.Name,
            LocalActions: localActions,
            EffectiveActions: effectiveActions,
            LocalPolicies: stage.Policies.ToArray(),
            EffectivePolicies: effectiveStage.EffectivePolicies.ToArray());

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
            SourceOwnsTarget: relationship.SourceOwnsTarget,
            Properties: relationship.Properties.ToArray(),
            Stages: relationship.Stages.ToArray(),
            Policies: relationship.Policies.ToArray());

        context.SetMetadata(relationship, new RelationshipCapabilityMetadata(view));
    }
}

public static class CapabilityAnalyzerExtensions {
    extension(AnalysisResult result) {
        public ActionCapabilityView GetCapabilityView(Action action) {
            ArgumentNullException.ThrowIfNull(action);

            return result.GetMetadata<ActionCapabilityMetadata>(action)?.View
                ?? throw new InvalidOperationException("Action capability view was not produced for the analysis request.");
        }

        public StageCapabilityView GetCapabilityView(Stage stage) {
            ArgumentNullException.ThrowIfNull(stage);

            return result.GetMetadata<StageCapabilityMetadata>(stage)?.View
                ?? throw new InvalidOperationException("Stage capability view was not produced for the analysis request.");
        }

        public RelationshipCapabilityView GetCapabilityView(Relationship relationship) {
            ArgumentNullException.ThrowIfNull(relationship);

            return result.GetMetadata<RelationshipCapabilityMetadata>(relationship)?.View
                ?? throw new InvalidOperationException("Relationship capability view was not produced for the analysis request.");
        }
    }
}