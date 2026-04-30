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
        // Effect validation logic migrated from Effect.Validate implementations:
        switch (effect) {
            case PublishEvent publishEvent:
                if (publishEvent.Event == null) {
                    context.ReportError(action, $"PublishEvent effect is missing Event.", DomainModelDiagnosticCodes.EffectBinding);
                    break;
                }
                if (!ReferenceEquals(publishEvent.Event.Domain, ownerEntity.Domain)) {
                    context.ReportError(action, $"PublishEvent event '{publishEvent.Event.Name}' does not belong to the same domain as entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                }
                foreach (var eventProperty in publishEvent.Event.Properties) {
                    if (!publishEvent.HasBindingFor(eventProperty)) {
                        context.ReportError(action, $"PublishEvent for '{publishEvent.Event.Name}' is missing binding for event property '{eventProperty.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                    }
                }
                break;
            case InvokeAction invokeAction:
                if (invokeAction.TargetAction == null) {
                    context.ReportError(action, $"InvokeAction effect is missing TargetAction.", DomainModelDiagnosticCodes.EffectBinding);
                    break;
                }
                if (!ReferenceEquals(invokeAction.TargetAction.Domain, ownerEntity.Domain)) {
                    context.ReportError(action, $"InvokeAction target action '{invokeAction.TargetAction.Name}' does not belong to the same domain as entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                }
                var propertyParameters = invokeAction.TargetAction.Parameters.OfType<Property>().ToArray();
                if (propertyParameters.Length != invokeAction.TargetAction.Parameters.Count) {
                    context.ReportError(action, $"InvokeAction for '{invokeAction.TargetAction.Name}' supports property parameters only.", DomainModelDiagnosticCodes.EffectBinding);
                }
                foreach (var targetParameter in propertyParameters) {
                    if (!invokeAction.HasBindingFor(targetParameter)) {
                        context.ReportError(action, $"InvokeAction for '{invokeAction.TargetAction.Name}' is missing binding for parameter '{targetParameter.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                    }
                }
                break;
            case CreateEntityInstance createEntityInstance:
                if (createEntityInstance.EntityType == null) {
                    context.ReportError(action, $"CreateEntityInstance effect is missing EntityType.", DomainModelDiagnosticCodes.EffectBinding);
                    break;
                }
                if (!ReferenceEquals(createEntityInstance.EntityType.Domain, ownerEntity.Domain)) {
                    context.ReportError(action, $"CreateEntityInstance entity type '{createEntityInstance.EntityType.Name}' does not belong to the same domain as entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                }
                if (createEntityInstance.InitialStage != null) {
                    if (!ReferenceEquals(createEntityInstance.InitialStage.Domain, ownerEntity.Domain)) {
                        context.ReportError(action, $"CreateEntityInstance initial stage '{createEntityInstance.InitialStage.Name}' does not belong to the same domain as entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                    }
                    if (!createEntityInstance.EntityType.Stages.Contains(createEntityInstance.InitialStage)) {
                        context.ReportError(action, $"Initial stage '{createEntityInstance.InitialStage.Name}' must belong to entity '{createEntityInstance.EntityType.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                    }
                }
                break;
            case StageTransition stageTransition:
                if (stageTransition.TargetStage == null) {
                    context.ReportError(action, $"StageTransition effect is missing TargetStage.", DomainModelDiagnosticCodes.EffectBinding);
                    break;
                }
                if (!ReferenceEquals(stageTransition.TargetStage.Domain, ownerEntity.Domain)) {
                    context.ReportError(action, $"StageTransition target stage '{stageTransition.TargetStage.Name}' does not belong to the same domain as entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                }
                if (ownerEntity.Stages.Count > 0 && !ownerEntity.Stages.Contains(stageTransition.TargetStage)) {
                    context.ReportError(action, $"Target stage '{stageTransition.TargetStage.Name}' must belong to entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                }
                break;
            case Poly.Data.Modeling.Effects.Mutations.Assign assign:
                if (assign.Target == null || assign.Value == null) {
                    context.ReportError(action, $"Assign effect is missing Target or Value.", DomainModelDiagnosticCodes.EffectBinding);
                    break;
                }
                if (!ReferenceEquals(assign.Target.Domain, ownerEntity.Domain) || !ReferenceEquals(assign.Value.Domain, ownerEntity.Domain)) {
                    context.ReportError(action, $"Assign effect Target/Value do not belong to the same domain as entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                }
                if (!ReferenceEquals(assign.Target.Type, assign.Value.Type)) {
                    context.ReportError(action, $"Assign effect requires matching types for target and value, but got '{assign.Target.Type.Name}' and '{assign.Value.Type.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                }
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