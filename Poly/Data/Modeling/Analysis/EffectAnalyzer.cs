using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.Effects.Mutations;
using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

internal sealed class EffectAnalyzer : INodeAnalyzer {
    public static string PassId => "DataEffectAnalyzer";
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        switch (node) {
            case Domain request:
                AnalyzeDomain(context, request.Domain);
                break;
            case Action action:
                AnalyzeAction(context, action);
                break;
            case EventSubscription subscription:
                AnalyzeSubscription(context, subscription);
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

                AnalyzeAction(context, action);
            }

            foreach (var subscription in entity.EventSubscriptions.Where(context.ShouldAnalyze)) {
                AnalyzeSubscription(context, subscription);
            }
        }
    }

    private static void AnalyzeAction(AnalysisContext context, Action action) {
        if (!context.TryBeginAnalyzerVisit<EffectAnalyzer>(action)) {
            return;
        }

        var ownerEntity = action.Entity;
        if (ownerEntity is null) {
            return;
        }

        if (!ValidateEffectBindings(context, action)) {
            return;
        }

        var coveredProperties = ComputeCoveredProperties(action);
        context.SetMetadata(action, new ActionCoverageMetadata(coveredProperties));

        foreach (var effect in action.Effects) {
            switch (effect) {
                case StageTransition transition:
                    ValidateStageTransition(context, action, ownerEntity, transition);
                    break;
                case CreateEntityInstance create:
                    ValidateCreateEntityInstance(context, action, create);
                    break;
                case InvokeAction invoke:
                    ValidateInvokeAction(context, action, invoke);
                    break;
            }
        }
    }

    private static bool ValidateEffectBindings(AnalysisContext context, Action action) {
        var ownerEntity = action.Entity;
        if (ownerEntity is null) {
            return false;
        }

        var isValid = true;

        foreach (var effect in action.Effects) {
            if (!ValidateEffect(context, action, ownerEntity, effect)) {
                isValid = false;
            }
        }

        return isValid;
    }

    private static bool ValidateEffect(AnalysisContext context, Action action, Entity ownerEntity, Effect effect) {
        switch (effect) {
            case PublishEvent publishEvent:
                if (publishEvent.Event == null) {
                    context.ReportError(action, $"PublishEvent effect is missing Event.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                if (!ReferenceEquals(publishEvent.Event.Domain, ownerEntity.Domain)) {
                    context.ReportError(action, $"PublishEvent event '{publishEvent.Event.Name}' does not belong to the same domain as entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                foreach (var eventProperty in publishEvent.Event.Properties) {
                    if (!publishEvent.PropertyBindings.TryGetValue(eventProperty.Name, out var bindingSource)) {
                        context.ReportError(action, $"PublishEvent for '{publishEvent.Event.Name}' is missing binding for event property '{eventProperty.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                        return false;
                    }
                    switch (bindingSource) {
                        case EventPropertyBindingSource.ActionParameter ap: {
                                var param = action.Parameters.OfType<Property>().FirstOrDefault(p => string.Equals(p.Name, ap.ParameterName, StringComparison.Ordinal));
                                if (param is null) {
                                    context.ReportError(action, $"PublishEvent binding for event property '{eventProperty.Name}' references action parameter '{ap.ParameterName}' which does not exist on action '{action.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                                    return false;
                                }
                                if (!DomainTypeAssignability.CanAssign(eventProperty.Type, param.Type)) {
                                    context.ReportError(action, $"PublishEvent binding for event property '{eventProperty.Name}': action parameter '{ap.ParameterName}' has type '{param.Type.Name}' but event property expects '{eventProperty.Type.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                                    return false;
                                }
                                break;
                            }
                        case EventPropertyBindingSource.EntityProperty ep: {
                                var prop = ownerEntity.Properties.FirstOrDefault(p => string.Equals(p.Name, ep.PropertyName, StringComparison.Ordinal));
                                if (prop is null) {
                                    context.ReportError(action, $"PublishEvent binding for event property '{eventProperty.Name}' references entity property '{ep.PropertyName}' which does not exist on entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                                    return false;
                                }
                                if (!DomainTypeAssignability.CanAssign(eventProperty.Type, prop.Type)) {
                                    context.ReportError(action, $"PublishEvent binding for event property '{eventProperty.Name}': entity property '{ep.PropertyName}' has type '{prop.Type.Name}' but event property expects '{eventProperty.Type.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                                    return false;
                                }
                                break;
                            }
                    }
                }
                break;
            case InvokeAction invokeAction:
                if (invokeAction.TargetAction == null) {
                    context.ReportError(action, $"InvokeAction effect is missing TargetAction.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                if (!ReferenceEquals(invokeAction.TargetAction.Domain, ownerEntity.Domain)) {
                    context.ReportError(action, $"InvokeAction target action '{invokeAction.TargetAction.Name}' does not belong to the same domain as entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                var propertyParameters = invokeAction.TargetAction.Parameters.OfType<Property>().ToArray();
                if (propertyParameters.Length != invokeAction.TargetAction.Parameters.Count) {
                    context.ReportError(action, $"InvokeAction for '{invokeAction.TargetAction.Name}' supports property parameters only.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                foreach (var targetParameter in propertyParameters) {
                    if (!invokeAction.HasBindingFor(targetParameter)) {
                        context.ReportError(action, $"InvokeAction for '{invokeAction.TargetAction.Name}' is missing binding for parameter '{targetParameter.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                        return false;
                    }
                }
                break;
            case CreateEntityInstance createEntityInstance:
                if (createEntityInstance.EntityType == null) {
                    context.ReportError(action, $"CreateEntityInstance effect is missing EntityType.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                if (!ReferenceEquals(createEntityInstance.EntityType.Domain, ownerEntity.Domain)) {
                    context.ReportError(action, $"CreateEntityInstance entity type '{createEntityInstance.EntityType.Name}' does not belong to the same domain as entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                if (createEntityInstance.InitialStage != null) {
                    if (!ReferenceEquals(createEntityInstance.InitialStage.Domain, ownerEntity.Domain)) {
                        context.ReportError(action, $"CreateEntityInstance initial stage '{createEntityInstance.InitialStage.Name}' does not belong to the same domain as entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                        return false;
                    }
                    if (!createEntityInstance.EntityType.Stages.Contains(createEntityInstance.InitialStage)) {
                        context.ReportError(action, $"Initial stage '{createEntityInstance.InitialStage.Name}' must belong to entity '{createEntityInstance.EntityType.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                        return false;
                    }
                }
                break;
            case DeleteEntityInstance deleteEntityInstance:
                if (deleteEntityInstance.EntityType == null) {
                    context.ReportError(action, $"DeleteEntityInstance effect is missing EntityType.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                if (!ReferenceEquals(deleteEntityInstance.EntityType.Domain, ownerEntity.Domain)) {
                    context.ReportError(action, $"DeleteEntityInstance entity type '{deleteEntityInstance.EntityType.Name}' does not belong to the same domain as entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                break;
            case StageTransition stageTransition:
                if (stageTransition.TargetStage == null) {
                    context.ReportError(action, $"StageTransition effect is missing TargetStage.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                if (!ReferenceEquals(stageTransition.TargetStage.Domain, ownerEntity.Domain)) {
                    context.ReportError(action, $"StageTransition target stage '{stageTransition.TargetStage.Name}' does not belong to the same domain as entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                if (ownerEntity.Stages.Count > 0 && !ownerEntity.Stages.Contains(stageTransition.TargetStage)) {
                    context.ReportError(action, $"Target stage '{stageTransition.TargetStage.Name}' must belong to entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                break;
            case TransitionRelationship transitionRelationship:
                if (transitionRelationship.Relationship == null) {
                    context.ReportError(action, $"TransitionRelationship effect is missing Relationship.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                if (!ReferenceEquals(transitionRelationship.Relationship.Domain, ownerEntity.Domain)) {
                    context.ReportError(action, $"TransitionRelationship '{transitionRelationship.Relationship.Name}' does not belong to the same domain as entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                if (transitionRelationship.TargetStage == null) {
                    context.ReportError(action, $"TransitionRelationship effect is missing TargetStage.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                if (!ReferenceEquals(transitionRelationship.TargetStage.Domain, ownerEntity.Domain)) {
                    context.ReportError(action, $"TransitionRelationship target stage '{transitionRelationship.TargetStage.Name}' does not belong to the same domain as entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                if (transitionRelationship.Relationship.Stages.Count > 0 && !transitionRelationship.Relationship.Stages.Contains(transitionRelationship.TargetStage)) {
                    context.ReportError(action, $"Target stage '{transitionRelationship.TargetStage.Name}' must belong to relationship '{transitionRelationship.Relationship.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                break;
            case LinkRelationship linkRelationship:
                if (linkRelationship.Relationship == null) {
                    context.ReportError(action, $"LinkRelationship effect is missing Relationship.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                if (!ReferenceEquals(linkRelationship.Relationship.Domain, ownerEntity.Domain)) {
                    context.ReportError(action, $"LinkRelationship '{linkRelationship.Relationship.Name}' does not belong to the same domain as entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                if (linkRelationship.Target == null) {
                    context.ReportError(action, $"LinkRelationship effect is missing Target.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                if (!ReferenceEquals(linkRelationship.Target.Domain, ownerEntity.Domain)) {
                    context.ReportError(action, $"LinkRelationship target '{linkRelationship.Target.Name}' does not belong to the same domain as entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                break;
            case UnlinkRelationship unlinkRelationship:
                if (unlinkRelationship.Relationship == null) {
                    context.ReportError(action, $"UnlinkRelationship effect is missing Relationship.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                if (!ReferenceEquals(unlinkRelationship.Relationship.Domain, ownerEntity.Domain)) {
                    context.ReportError(action, $"UnlinkRelationship '{unlinkRelationship.Relationship.Name}' does not belong to the same domain as entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                if (unlinkRelationship.Target == null) {
                    context.ReportError(action, $"UnlinkRelationship effect is missing Target.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                if (!ReferenceEquals(unlinkRelationship.Target.Domain, ownerEntity.Domain)) {
                    context.ReportError(action, $"UnlinkRelationship target '{unlinkRelationship.Target.Name}' does not belong to the same domain as entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                break;
            case Assign assign:
                if (assign.Target == null || assign.Value == null) {
                    context.ReportError(action, $"Assign effect is missing Target or Value.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                if (!ReferenceEquals(assign.Target.Domain, ownerEntity.Domain) || !ReferenceEquals(assign.Value.Domain, ownerEntity.Domain)) {
                    context.ReportError(action, $"Assign effect Target/Value do not belong to the same domain as entity '{ownerEntity.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                if (!DomainTypeAssignability.CanAssign(assign.Target.Type, assign.Value.Type)) {
                    context.ReportError(action, $"Assign effect requires matching types for target and value, but got '{assign.Target.Type.Name}' and '{assign.Value.Type.Name}'.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                break;
            case Effects.Conditional conditional:
                if (conditional.Condition == null) {
                    context.ReportError(action, $"Conditional effect is missing Condition.", DomainModelDiagnosticCodes.EffectBinding);
                    return false;
                }
                foreach (var childEffect in conditional.ChildEffects) {
                    if (!ValidateEffect(context, action, ownerEntity, childEffect)) {
                        return false;
                    }
                }
                break;
            case Composite composite:
                foreach (var childEffect in composite.ChildEffects) {
                    if (!ValidateEffect(context, action, ownerEntity, childEffect)) {
                        return false;
                    }
                }
                break;
        }

        return true;
    }

    private static void AnalyzeSubscription(AnalysisContext context, EventSubscription subscription) {
        if (!context.TryBeginAnalyzerVisit<EffectAnalyzer>(subscription)) {
            return;
        }

        if (!ReferenceEquals(subscription.ConsumerEntity.Domain, subscription.Domain)) {
            context.ReportError(subscription, $"Event subscription consumer entity '{subscription.ConsumerEntity.Name}' does not belong to domain '{subscription.Domain.Name}'.", DomainModelDiagnosticCodes.EventSubscription);
            return;
        }

        if (!ReferenceEquals(subscription.EventType.Domain, subscription.Domain)) {
            context.ReportError(subscription, $"Event subscription event '{subscription.EventType.Name}' does not belong to domain '{subscription.Domain.Name}'.", DomainModelDiagnosticCodes.EventSubscription);
            return;
        }

        if (!ReferenceEquals(subscription.HandlerAction.Domain, subscription.Domain)) {
            context.ReportError(subscription, $"Event subscription handler '{subscription.HandlerAction.Name}' does not belong to domain '{subscription.Domain.Name}'.", DomainModelDiagnosticCodes.EventSubscription);
            return;
        }

        if (!ReferenceEquals(subscription.HandlerAction.Entity, subscription.ConsumerEntity)) {
            context.ReportError(subscription, $"Event subscription handler '{subscription.HandlerAction.Name}' must belong to consumer entity '{subscription.ConsumerEntity.Name}'.", DomainModelDiagnosticCodes.EventSubscription);
            return;
        }

        if (!subscription.ConsumerEntity.Actions.Contains(subscription.HandlerAction)) {
            context.ReportError(subscription, $"Event subscription handler '{subscription.HandlerAction.Name}' is not registered on consumer entity '{subscription.ConsumerEntity.Name}'.", DomainModelDiagnosticCodes.EventSubscription);
            return;
        }

        if (string.IsNullOrWhiteSpace(subscription.EventParameterName)) {
            context.ReportError(subscription, $"Event subscription '{subscription.Name}' requires a non-empty event parameter name.", DomainModelDiagnosticCodes.EventSubscription);
            return;
        }

        var eventParameter = subscription.HandlerAction.Parameters
            .OfType<Property>()
            .FirstOrDefault(parameter => string.Equals(parameter.Name, subscription.EventParameterName, StringComparison.Ordinal));
        if (eventParameter is null) {
            context.ReportError(subscription, $"Event subscription '{subscription.Name}' references missing handler parameter '{subscription.EventParameterName}' on action '{subscription.HandlerAction.Name}'.", DomainModelDiagnosticCodes.EventSubscription);
            return;
        }

        if (!DomainTypeAssignability.CanAssign(eventParameter.Type, subscription.EventType)) {
            context.ReportError(subscription, $"Event subscription '{subscription.Name}' parameter '{subscription.EventParameterName}' has type '{eventParameter.Type.Name}' but must accept '{subscription.EventType.Name}'.", DomainModelDiagnosticCodes.EventSubscription);
            return;
        }

        if (subscription.ConsumerEntity.EventSubscriptions.Count(candidate => ReferenceEquals(candidate.HandlerAction, subscription.HandlerAction)) > 1) {
            context.ReportError(subscription, $"Handler action '{subscription.HandlerAction.Name}' cannot have more than one event subscription.", DomainModelDiagnosticCodes.EventSubscription);
            return;
        }

        if (subscription.RoutingMode is EventSubscriptionRoutingMode.Correlated && subscription.Correlations.Count == 0) {
            context.ReportError(subscription, $"Correlated event subscription '{subscription.Name}' requires at least one correlation binding.", DomainModelDiagnosticCodes.EventSubscription);
            return;
        }

        foreach (var binding in subscription.Correlations) {
            var eventProperty = subscription.EventType.Properties.FirstOrDefault(property => string.Equals(property.Name, binding.EventPropertyName, StringComparison.Ordinal));
            if (eventProperty is null) {
                context.ReportError(subscription, $"Event subscription correlation references missing event property '{binding.EventPropertyName}' on event '{subscription.EventType.Name}'.", DomainModelDiagnosticCodes.EventSubscription);
                return;
            }

            var consumerProperty = subscription.ConsumerEntity.Properties.FirstOrDefault(property => string.Equals(property.Name, binding.ConsumerPropertyName, StringComparison.Ordinal));
            if (consumerProperty is null) {
                context.ReportError(subscription, $"Event subscription correlation references missing consumer property '{binding.ConsumerPropertyName}' on entity '{subscription.ConsumerEntity.Name}'.", DomainModelDiagnosticCodes.EventSubscription);
                return;
            }

            if (!DomainTypeAssignability.CanAssign(consumerProperty.Type, eventProperty.Type)) {
                context.ReportError(subscription, $"Event subscription correlation '{binding.EventPropertyName}->{binding.ConsumerPropertyName}' has mismatched types '{eventProperty.Type.Name}' and '{consumerProperty.Type.Name}'.", DomainModelDiagnosticCodes.EventSubscription);
                return;
            }
        }
    }

    private static IReadOnlySet<Property> ComputeCoveredProperties(Action action) {
        var covered = new HashSet<Property>(EqualityComparer<Property>.Default);

        foreach (var effect in action.Effects) {
            switch (effect) {
                case Assign assign when assign.Target is Property property:
                    covered.Add(property);
                    break;
                case CreateEntityInstance create when create.InitialStage is not null:
                    var stageReq = PolicyConstraintHelpers.ComputeRequiredProperties(create.EntityType, create.InitialStage);
                    foreach (var req in stageReq) {
                        covered.Add(req);
                    }
                    break;
            }
        }

        return covered;
    }

    private static void ValidateStageTransition(AnalysisContext context, Action action, Entity ownerEntity, StageTransition transition) {
        var targetStage = transition.TargetStage;
        if (targetStage is null) {
            return;
        }

        var metadata = context.GetMetadata<StageTransitionRequirementAnalysisMetadata>(targetStage);
        var transitionReq = metadata?.Analysis;

        if (transitionReq is null) {
            return;
        }

        var covered = ComputeCoveredProperties(action);
        var entityPropertyNames = ownerEntity.Properties
            .Select(static p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var newlyRequired in transitionReq.NewlyRequiredProperties) {
            if (!entityPropertyNames.Contains(newlyRequired.Name)) {
                continue;
            }

            if (!covered.Contains(newlyRequired)) {
                context.ReportError(
                    action,
                    $"Action '{action.Name}' transitions to stage '{targetStage.Name}' which requires property '{newlyRequired.Name}', but no effect produces a value for it.",
                    DomainModelDiagnosticCodes.EffectUnsatisfiedRequirement);
            }
        }
    }

    private static void ValidateCreateEntityInstance(AnalysisContext context, Action action, CreateEntityInstance create) {
        if (create.EntityType is null || create.InitialStage is null) {
            return;
        }

        var requiredProperties = PolicyConstraintHelpers.ComputeRequiredProperties(create.EntityType, create.InitialStage);
        var covered = ComputeCoveredProperties(action);

        foreach (var required in requiredProperties) {
            if (!covered.Contains(required)) {
                context.ReportError(
                    action,
                    $"CreateEntityInstance for '{create.EntityType.Name}' requires property '{required.Name}' at initial stage '{create.InitialStage.Name}', but no effect produces a value for it.",
                    DomainModelDiagnosticCodes.EffectUnsatisfiedRequirement);
            }
        }
    }

    private static void ValidateInvokeAction(AnalysisContext context, Action action, InvokeAction invoke) {
        if (invoke.TargetAction is null) {
            return;
        }

        var targetCoverage = context.GetMetadata<ActionCoverageMetadata>(invoke.TargetAction);
        if (targetCoverage is null) {
            return;
        }

        foreach (var boundParam in invoke.ParameterBindings.Values.OfType<Property>()) {
            if (!targetCoverage.CoveredProperties.Contains(boundParam)) {
                context.ReportWarning(
                    action,
                    $"InvokeAction invokes '{invoke.TargetAction.Name}' with parameter '{boundParam.Name}', but the target action may not produce a value for it.",
                    DomainModelDiagnosticCodes.EffectUnsatisfiedRequirement);
            }
        }
    }
}

public static class EffectAnalyzerExtensions {
    extension(AnalysisResult result) {
        public IReadOnlySet<Property> GetActionCoverage(Action action) {
            ArgumentNullException.ThrowIfNull(action);

            return result.GetMetadata<ActionCoverageMetadata>(action)?.CoveredProperties
                ?? throw new InvalidOperationException("Action coverage was not produced for the analysis request.");
        }
    }
}