using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.Effects.Mutations;

namespace Poly.Data.Modeling;

internal sealed class EffectBindingAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        switch (node) {
            case Domain request:
                AnalyzeDomain(context, request.Domain);
                break;
            case Action action:
                ValidateActionEffects(context, action.Entity, action);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        foreach (var entity in domain.Types.OfType<Entity>().Where(context.ShouldAnalyze)) {
            foreach (var action in entity.Actions.Concat(entity.Stages.SelectMany(static stage => stage.Actions))) {
                if (!context.ShouldAnalyze(action)) {
                    continue;
                }

                ValidateActionEffects(context, entity, action);
            }
        }
    }

    private static void ValidateActionEffects(AnalysisContext context, Entity ownerEntity, Action action) {
        foreach (var effect in action.Effects) {
            ValidateEffect(context, ownerEntity, action, effect);
        }
    }

    private static void ValidateEffect(AnalysisContext context, Entity ownerEntity, Action action, Effect effect) {
        try {
            effect.Validate(ownerEntity);
        }
        catch (InvalidOperationException ex) {
            context.ReportError(
                action,
                $"Action '{action.Name}' has invalid effect '{effect.GetType().Name}': {ex.Message}",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }

        switch (effect) {
            case PublishEvent publishEvent:
                ValidatePublishEventBindings(context, action, publishEvent);
                break;
            case InvokeAction invokeAction:
                ValidateInvokeActionBindings(context, action, invokeAction);
                break;
            case CreateEntityInstance createEntityInstance:
                ValidateCreateBindings(context, action, createEntityInstance);
                break;
            case StageTransition:
            case Assign:
                break;
        }
    }

    private static void ValidatePublishEventBindings(AnalysisContext context, Action action, PublishEvent publishEvent) {
        foreach (var eventProperty in publishEvent.Event.Properties) {
            if (!publishEvent.HasBindingFor(eventProperty)) {
                context.ReportError(
                    action,
                    $"PublishEvent for '{publishEvent.Event.Name}' is missing binding for '{eventProperty.Name}'.",
                    DomainModelDiagnosticCodes.EffectBinding);
            }
        }
    }

    private static void ValidateInvokeActionBindings(AnalysisContext context, Action action, InvokeAction invokeAction) {
        foreach (var targetParameter in invokeAction.TargetAction.Parameters.OfType<Property>()) {
            if (!invokeAction.HasBindingFor(targetParameter)) {
                context.ReportError(
                    action,
                    $"InvokeAction for '{invokeAction.TargetAction.Name}' is missing binding for '{targetParameter.Name}'.",
                    DomainModelDiagnosticCodes.EffectBinding);
            }
        }
    }

    // private Stage? ResolveInitialStage() {
    //     if (InitialStage is null) {
    //         return null;
    //     }

    //     if (!EntityType.Stages.Contains(InitialStage)) {
    //         throw new InvalidOperationException($"Initial stage '{InitialStage.Name}' must belong to entity '{EntityType.Name}'.");
    //     }

    //     return InitialStage;
    // }

    private static void ValidateCreateBindings(AnalysisContext context, Action action, CreateEntityInstance createEntityInstance) {
        // var required = createEntityInstance.GetRequiredProperties();
        // foreach (var requiredProperty in required) {
        //     if (!action.Parameters.OfType<Property>().Any(parameter => string.Equals(parameter.Name, requiredProperty.Name, StringComparison.Ordinal))) {
        //         context.ReportWarning(
        //             action,
        //             $"CreateEntityInstance may require '{requiredProperty.Name}', but action '{action.Name}' has no matching parameter.",
        //             DomainModelDiagnosticCodes.EffectBinding);
        //     }
        // }
    }
}