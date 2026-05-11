using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

public static class MutationApply {
    public static AnalysisResult AddType(Domain domain, DomainType type) => Apply(domain, mutation => mutation.AddType(type));

    public static AnalysisResult AddRelationship(Domain domain, Relationship relationship) => Apply(domain, mutation => mutation.AddRelationship(relationship));

    public static AnalysisResult AddRelationship(Entity entity, Relationship relationship) => Apply(entity.Domain, mutation => mutation.AddEntityRelationship(entity, relationship));

    public static AnalysisResult AddProperty(Entity entity, Property property) => Apply(entity.Domain, mutation => mutation.AddProperty(entity, property));

    public static AnalysisResult AddProperty(Event @event, Property property) => Apply(@event.Domain, mutation => mutation.AddProperty(@event, property));

    public static AnalysisResult AddProperty(Relationship relationship, Property property) => Apply(relationship.Domain, mutation => mutation.AddProperty(relationship, property));

    public static AnalysisResult AddStage(Entity entity, Stage stage) => Apply(entity.Domain, mutation => mutation.AddStage(entity, stage));

    public static AnalysisResult AddStage(Relationship relationship, Stage stage) => Apply(relationship.Domain, mutation => mutation.AddStage(relationship, stage));

    public static AnalysisResult AddPolicy(Entity entity, Policy policy) => Apply(entity.Domain, mutation => mutation.AddPolicy(entity, policy));

    public static AnalysisResult AddPolicy(Stage stage, Policy policy) => Apply(stage.Domain, mutation => mutation.AddPolicy(stage, policy));

    public static AnalysisResult AddPolicy(Property property, Policy policy) => Apply(property.Domain, mutation => mutation.AddPolicy(property, policy));

    public static AnalysisResult AddPolicy(Relationship relationship, Policy policy) => Apply(relationship.Domain, mutation => mutation.AddPolicy(relationship, policy));

    public static AnalysisResult AddPolicy(Action action, Policy policy) => Apply(action.Domain, mutation => mutation.AddPolicy(action, policy));

    public static AnalysisResult RemovePolicy(Stage stage, Policy policy) => Apply(stage.Domain, mutation => mutation.RemovePolicy(stage, policy));

    public static AnalysisResult RemovePolicy(Action action, Policy policy) => Apply(action.Domain, mutation => mutation.RemovePolicy(action, policy));

    public static AnalysisResult AddAction(Entity entity, Action action) => Apply(entity.Domain, mutation => mutation.AddAction(entity, action));

    public static AnalysisResult AddAction(Stage stage, Action action) => Apply(stage.Domain, mutation => mutation.AddAction(stage, action));

    public static AnalysisResult AddEvent(Entity entity, Event @event) => Apply(entity.Domain, mutation => mutation.AddEvent(entity, @event));

    public static AnalysisResult AddEventSubscription(Entity entity, EventSubscription subscription) => Apply(entity.Domain, mutation => mutation.AddEventSubscription(entity, subscription));

    public static AnalysisResult AddParameter(Action action, Property parameter) => Apply(action.Domain, mutation => mutation.AddParameter(action, parameter));

    public static AnalysisResult AddEventSubscriptionCorrelation(EventSubscription subscription, EventCorrelationBinding binding) =>
        Apply(subscription.Domain, mutation => mutation.AddEventSubscriptionCorrelation(subscription, binding));

    public static AnalysisResult SetEventSubscriptionAudience(EventSubscription subscription, EventSubscriptionAudience audience) =>
        Apply(subscription.Domain, mutation => mutation.SetEventSubscriptionAudience(subscription, audience));

    public static AnalysisResult AddEffect(Action action, Effect effect) => Apply(action.Domain, mutation => mutation.AddEffect(action, effect));

    public static AnalysisResult AddEffect(Effects.Conditional conditional, Effect effect) => Apply(conditional.Domain, mutation => mutation.AddEffect(conditional, effect));

    public static AnalysisResult AddEffect(Effects.Composite composite, Effect effect) => Apply(composite.Domain, mutation => mutation.AddEffect(composite, effect));

    public static AnalysisResult SetEffectOutput(Effect effect, string outputName, DomainType type) =>
        Apply(effect.Domain, mutation => mutation.SetEffectOutput(effect, outputName, type));

    public static AnalysisResult BindOutputTo(Effect sourceEffect, string outputName, Effect targetEffect, string targetParamName) =>
        Apply(sourceEffect.Domain, mutation => mutation.BindOutputTo(sourceEffect, outputName, targetEffect, targetParamName));

    public static AnalysisResult BindParameter(InvokeAction effect, Property targetParameter, DomainValue value) =>
        Apply(effect.Domain, mutation => mutation.BindParameter(effect, targetParameter, value));

    public static AnalysisResult BindParameterFrom(InvokeAction effect, string targetParamName, Effect sourceEffect, string sourceOutputName) =>
        Apply(effect.Domain, mutation => mutation.BindParameterFrom(effect, targetParamName, sourceEffect, sourceOutputName));

    public static AnalysisResult SetEventHandlerTrigger(Action action, Event eventType, string eventParameterName) =>
        Apply(action.Domain, mutation => mutation.SetEventHandlerTrigger(action, eventType, eventParameterName));

    public static AnalysisResult AddRule(Policy policy, Rule rule) => Apply(policy.Domain, mutation => mutation.AddRule(policy, rule));

    public static AnalysisResult AddConstraint(Property property, Constraint constraint) => Apply(property.Domain, mutation => mutation.AddConstraint(property, constraint));

    private static AnalysisResult Apply(Domain domain, Func<Domain.Mutation, Domain.Mutation> applyMutation) {
        var mutation = applyMutation(domain.CreateMutation());
        return mutation.Apply();
    }
}