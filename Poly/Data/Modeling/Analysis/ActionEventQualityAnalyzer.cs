using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.Effects.Mutations;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;

namespace Poly.Data.Modeling;

using EffectConditional = Poly.Data.Modeling.Effects.Conditional;

internal sealed class ActionEventQualityAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        switch (node) {
            case Domain request:
                AnalyzeDomain(context, request.Domain);
                break;
            case Entity entity:
                AnalyzeEntity(context, entity);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        if (!context.TryBeginAnalyzerVisit<ActionEventQualityAnalyzer>(domain)) {
            return;
        }

        var entities = domain.Entities.Where(context.ShouldAnalyze).ToArray();
        var publishedEvents = CollectPublishedEvents(entities);
        var subscribedEvents = entities
            .SelectMany(static entity => entity.EventSubscriptions)
            .Select(static subscription => subscription.EventType)
            .ToHashSet(EqualityComparer<Event>.Default);

        var publishedActionGraph = BuildActionGraph(entities);
        var actionCycles = FindActionCycles(publishedActionGraph);
        foreach (var cycleNode in actionCycles) {
            context.ReportWarning(
                cycleNode,
                $"Action '{cycleNode.Name}' participates in a causality cycle across invokes/published events.",
                DomainModelDiagnosticCodes.ActionOrderingCausality);
        }

        foreach (var domainEvent in domain.Types.OfType<Event>()) {
            if (!publishedEvents.Contains(domainEvent)) {
                context.ReportHint(
                    domainEvent,
                    $"Event '{domainEvent.Name}' is not observed as published by any action.",
                    DomainModelDiagnosticCodes.EventFlowLiveness);
            }
        }

        foreach (var published in publishedEvents) {
            if (!subscribedEvents.Contains(published)) {
                context.ReportHint(
                    published,
                    $"Published event '{published.Name}' is not observed by any subscription.",
                    DomainModelDiagnosticCodes.EventFlowLiveness);
            }
        }
    }

    private static void AnalyzeEntity(AnalysisContext context, Entity entity) {
        if (!context.TryBeginAnalyzerVisit<ActionEventQualityAnalyzer>(entity)) {
            return;
        }

        foreach (var action in entity.Actions.Concat(entity.Stages.SelectMany(static stage => stage.Actions))) {
            if (!context.ShouldAnalyze(action)) {
                continue;
            }

            ValidateEventContract(context, action);
            ValidateActionOrdering(context, action);
            ValidateReplaySafety(context, action);
            ValidateRuleCoverage(context, action);
        }

        foreach (var subscription in entity.EventSubscriptions.Where(context.ShouldAnalyze)) {
            ValidateCorrelationSoundness(context, subscription);
        }
    }

    private static HashSet<Event> CollectPublishedEvents(IEnumerable<Entity> entities) {
        var published = new HashSet<Event>(EqualityComparer<Event>.Default);
        foreach (var action in entities.SelectMany(static entity => entity.Actions.Concat(entity.Stages.SelectMany(static stage => stage.Actions)))) {
            foreach (var effect in FlattenEffects(action.Effects)) {
                if (effect is PublishEvent publish && publish.Event is not null) {
                    published.Add(publish.Event);
                }
            }
        }

        return published;
    }

    private static Dictionary<Action, HashSet<Action>> BuildActionGraph(IEnumerable<Entity> entities) {
        var graph = new Dictionary<Action, HashSet<Action>>(EqualityComparer<Action>.Default);

        foreach (var entity in entities) {
            var actions = entity.Actions.Concat(entity.Stages.SelectMany(static stage => stage.Actions)).ToArray();
            foreach (var action in actions) {
                if (!graph.TryGetValue(action, out var edges)) {
                    edges = [];
                    graph[action] = edges;
                }

                foreach (var effect in FlattenEffects(action.Effects)) {
                    switch (effect) {
                        case InvokeAction invoke when invoke.TargetAction is not null:
                            edges.Add(invoke.TargetAction);
                            break;
                        case PublishEvent publish when publish.Event is not null:
                            foreach (var handler in entity.Domain.Entities
                                .SelectMany(static consumer => consumer.EventSubscriptions)
                                .Where(subscription => ReferenceEquals(subscription.EventType, publish.Event))
                                .Select(static subscription => subscription.HandlerAction)) {
                                edges.Add(handler);
                            }
                            break;
                    }
                }
            }
        }

        return graph;
    }

    private static IReadOnlyCollection<Action> FindActionCycles(Dictionary<Action, HashSet<Action>> graph) {
        var cycles = new HashSet<Action>(EqualityComparer<Action>.Default);
        var visiting = new HashSet<Action>(EqualityComparer<Action>.Default);
        var visited = new HashSet<Action>(EqualityComparer<Action>.Default);

        foreach (var node in graph.Keys) {
            Visit(node);
        }

        return cycles.ToArray();

        void Visit(Action action) {
            if (visited.Contains(action)) {
                return;
            }

            if (!visiting.Add(action)) {
                cycles.Add(action);
                return;
            }

            if (graph.TryGetValue(action, out var edges)) {
                foreach (var edge in edges) {
                    if (visiting.Contains(edge)) {
                        cycles.Add(action);
                        cycles.Add(edge);
                        continue;
                    }

                    Visit(edge);
                }
            }

            visiting.Remove(action);
            visited.Add(action);
        }
    }

    private static void ValidateEventContract(AnalysisContext context, Action action) {
        if (action.Trigger is not ActionTrigger.EventHandler trigger) {
            return;
        }

        var parameter = action.Parameters
            .OfType<Property>()
            .FirstOrDefault(p => string.Equals(p.Name, trigger.EventParameterName, StringComparison.Ordinal));
        if (parameter?.Type is not Event parameterEventType) {
            return;
        }

        foreach (var contractProperty in trigger.EventType.Properties) {
            var candidate = parameterEventType.Properties
                .FirstOrDefault(p => string.Equals(p.Name, contractProperty.Name, StringComparison.Ordinal));
            if (candidate is null) {
                context.ReportError(
                    action,
                    $"Action '{action.Name}' event contract is missing property '{contractProperty.Name}' required by event '{trigger.EventType.Name}'.",
                    DomainModelDiagnosticCodes.ActionEventContract);
                continue;
            }

            if (!DomainTypeAssignability.CanAssign(contractProperty.Type, candidate.Type)) {
                context.ReportError(
                    action,
                    $"Action '{action.Name}' event contract property '{candidate.Name}' has type '{candidate.Type.Name}' but expected '{contractProperty.Type.Name}'.",
                    DomainModelDiagnosticCodes.ActionEventContract);
            }
        }
    }

    private static void ValidateActionOrdering(AnalysisContext context, Action action) {
        var flattened = FlattenEffects(action.Effects).ToArray();
        var deleteIndex = Array.FindIndex(flattened, static effect => effect is DeleteEntityInstance);
        if (deleteIndex < 0) {
            return;
        }

        if (flattened.Skip(deleteIndex + 1).Any(static effect =>
            effect is Assign or CreateEntityInstance or StageTransition or LinkRelationship or UnlinkRelationship or TransitionRelationship)) {
            context.ReportWarning(
                action,
                $"Action '{action.Name}' has invalid post-state ordering: mutating effects execute after DeleteEntityInstance.",
                DomainModelDiagnosticCodes.EffectPrePostCondition);
        }
    }

    private static void ValidateReplaySafety(AnalysisContext context, Action action) {
        if (action.Trigger is not ActionTrigger.EventHandler) {
            return;
        }

        var replaySensitive = FlattenEffects(action.Effects).Any(static effect =>
            effect is CreateEntityInstance or LinkRelationship or PublishEvent);
        if (!replaySensitive) {
            return;
        }

        context.ReportWarning(
            action,
            $"Event-handler action '{action.Name}' has idempotency risk under replay because it performs create/link/publish effects.",
            DomainModelDiagnosticCodes.ActionIdempotencyReplay);
    }

    private static void ValidateRuleCoverage(AnalysisContext context, Action action) {
        if (action.Entity is null) {
            return;
        }

        var effectSet = FlattenEffects(action.Effects).ToArray();
        var hasRequirementSensitiveTransition = effectSet.Any(static effect => effect is StageTransition);
        if (!hasRequirementSensitiveTransition) {
            return;
        }

        var required = PolicyConstraintHelpers.ComputeRequiredProperties(action.Entity, stage: null);
        if (required.Count == 0) {
            return;
        }

        var covered = effectSet
            .OfType<Assign>()
            .Select(static assign => assign.Target)
            .OfType<Property>()
            .ToHashSet(EqualityComparer<Property>.Default);

        if (required.Any(property => !covered.Contains(property))) {
            context.ReportHint(
                action,
                $"Action '{action.Name}' has coverage gaps: one or more required properties are not explicitly assigned in mutation effects.",
                DomainModelDiagnosticCodes.RuleCoverage);
        }
    }

    private static void ValidateCorrelationSoundness(AnalysisContext context, EventSubscription subscription) {
        if (subscription.Audience is not EventSubscriptionAudience.Correlated) {
            return;
        }

        var duplicateEventKey = subscription.Correlations
            .GroupBy(static binding => binding.EventPropertyName, StringComparer.Ordinal)
            .Any(static group => group.Count() > 1);
        if (duplicateEventKey) {
            context.ReportWarning(
                subscription,
                $"Event subscription '{subscription.Name}' correlation maps the same event property more than once.",
                DomainModelDiagnosticCodes.EventCorrelationSoundness);
        }

        var duplicateConsumerKey = subscription.Correlations
            .GroupBy(static binding => binding.ConsumerPropertyName, StringComparer.Ordinal)
            .Any(static group => group.Count() > 1);
        if (duplicateConsumerKey) {
            context.ReportWarning(
                subscription,
                $"Event subscription '{subscription.Name}' correlation maps multiple event properties to the same consumer key.",
                DomainModelDiagnosticCodes.EventCorrelationSoundness);
        }

        var requiredConsumerProperties = subscription.ConsumerEntity.Properties
            .Where(static property => property.EffectiveConstraints.Any(static c => c.IsOrContains<RequiredConstraint>()))
            .ToArray();
        var correlatedConsumerNames = subscription.Correlations
            .Select(static binding => binding.ConsumerPropertyName)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var required in requiredConsumerProperties) {
            if (!correlatedConsumerNames.Contains(required.Name)) {
                context.ReportHint(
                    subscription,
                    $"Event subscription '{subscription.Name}' correlation does not include required consumer property '{required.Name}'.",
                    DomainModelDiagnosticCodes.EventCorrelationSoundness);
            }
        }
    }

    private static IEnumerable<Effect> FlattenEffects(IEnumerable<Effect> effects) {
        foreach (var effect in effects) {
            yield return effect;
            switch (effect) {
                case EffectConditional conditional:
                    foreach (var nested in FlattenEffects(conditional.ChildEffects)) {
                        yield return nested;
                    }
                    break;
                case Composite composite:
                    foreach (var nested in FlattenEffects(composite.ChildEffects)) {
                        yield return nested;
                    }
                    break;
            }
        }
    }
}