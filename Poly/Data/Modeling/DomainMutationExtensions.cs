using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

/// <summary>
/// Shorthand extensions for single-step mutations. Each method creates and immediately applies
/// a one-operation mutation. For multi-step transactional changes use <see cref="Domain.CreateMutation"/>.
/// </summary>
public static class DomainMutationExtensions {

    // ── Domain ───────────────────────────────────────────────────────────────

    public static AnalysisResult AddType(this Domain domain, DomainType type) =>
        domain.CreateMutation().AddType(type).Apply();

    public static AnalysisResult AddRelationship(this Domain domain, Relationship relationship) =>
        domain.CreateMutation().AddRelationship(relationship).Apply();

    public static AnalysisResult AddImportedContract(this Domain domain, ImportedContract contract) =>
        domain.CreateMutation().AddImportedContract(contract).Apply();

    public static AnalysisResult RemoveImportedContract(this Domain domain, ImportedContract contract) =>
        domain.CreateMutation().RemoveImportedContract(contract).Apply();

    public static AnalysisResult AddContractBinding(this Domain domain, ContractBinding binding) =>
        domain.CreateMutation().AddContractBinding(binding).Apply();

    public static AnalysisResult RemoveContractBinding(this Domain domain, ContractBinding binding) =>
        domain.CreateMutation().RemoveContractBinding(binding).Apply();

    // ── Entity ───────────────────────────────────────────────────────────────

    public static AnalysisResult AddProperty(this Entity entity, Property property) =>
        entity.Domain.CreateMutation().AddProperty(entity, property).Apply();

    public static AnalysisResult RemoveProperty(this Entity entity, Property property) =>
        entity.Domain.CreateMutation().RemoveProperty(entity, property).Apply();

    public static AnalysisResult AddStage(this Entity entity, Stage stage) =>
        entity.Domain.CreateMutation().AddStage(entity, stage).Apply();

    public static AnalysisResult RemoveStage(this Entity entity, Stage stage) =>
        entity.Domain.CreateMutation().RemoveStage(entity, stage).Apply();

    public static AnalysisResult AddPolicy(this Entity entity, Policy policy) =>
        entity.Domain.CreateMutation().AddPolicy(entity, policy).Apply();

    public static AnalysisResult RemovePolicy(this Entity entity, Policy policy) =>
        entity.Domain.CreateMutation().RemovePolicy(entity, policy).Apply();

    public static AnalysisResult AddAction(this Entity entity, Action action) =>
        entity.Domain.CreateMutation().AddAction(entity, action).Apply();

    public static AnalysisResult RemoveAction(this Entity entity, Action action) =>
        entity.Domain.CreateMutation().RemoveAction(entity, action).Apply();

    public static AnalysisResult AddEvent(this Entity entity, Event @event) =>
        entity.Domain.CreateMutation().AddEvent(entity, @event).Apply();

    public static AnalysisResult AddEventSubscription(this Entity entity, EventSubscription subscription) =>
        entity.Domain.CreateMutation().AddEventSubscription(entity, subscription).Apply();

    public static AnalysisResult RemoveEventSubscription(this Entity entity, EventSubscription subscription) =>
        entity.Domain.CreateMutation().RemoveEventSubscription(entity, subscription).Apply();

    public static AnalysisResult RemoveEvent(this Entity entity, Event @event) =>
        entity.Domain.CreateMutation().RemoveEvent(entity, @event).Apply();

    public static AnalysisResult AddRelationship(this Entity entity, Relationship relationship) =>
        entity.Domain.CreateMutation().AddEntityRelationship(entity, relationship).Apply();

    public static AnalysisResult RemoveRelationship(this Entity entity, Relationship relationship) =>
        entity.Domain.CreateMutation().RemoveEntityRelationship(entity, relationship).Apply();

    // ── Actor ────────────────────────────────────────────────────────────────

    public static AnalysisResult SetSubjectProperty(this Actor actor, Property? property) =>
        actor.Domain.CreateMutation().SetActorSubjectProperty(actor, property).Apply();

    public static AnalysisResult SetRoleClaimType(this Actor actor, string? roleClaimType) =>
        actor.Domain.CreateMutation().SetActorRoleClaimType(actor, roleClaimType).Apply();

    public static AnalysisResult AddClaimMapping(this Actor actor, ActorClaimMapping mapping) =>
        actor.Domain.CreateMutation().AddActorClaimMapping(actor, mapping).Apply();

    public static AnalysisResult RemoveClaimMapping(this Actor actor, ActorClaimMapping mapping) =>
        actor.Domain.CreateMutation().RemoveActorClaimMapping(actor, mapping).Apply();

    // ── Stage ────────────────────────────────────────────────────────────────

    public static AnalysisResult AddPolicy(this Stage stage, Policy policy) =>
        stage.Domain.CreateMutation().AddPolicy(stage, policy).Apply();

    public static AnalysisResult RemovePolicy(this Stage stage, Policy policy) =>
        stage.Domain.CreateMutation().RemovePolicy(stage, policy).Apply();

    public static AnalysisResult AddAction(this Stage stage, Action action) =>
        stage.Domain.CreateMutation().AddAction(stage, action).Apply();

    public static AnalysisResult RemoveAction(this Stage stage, Action action) =>
        stage.Domain.CreateMutation().RemoveAction(stage, action).Apply();

    // ── Policy ───────────────────────────────────────────────────────────────

    public static AnalysisResult AddRule(this Policy policy, Rule rule) =>
        policy.Domain.CreateMutation().AddRule(policy, rule).Apply();

    public static AnalysisResult RemoveRule(this Policy policy, Rule rule) =>
        policy.Domain.CreateMutation().RemoveRule(policy, rule).Apply();

    // ── Property ─────────────────────────────────────────────────────────────

    public static AnalysisResult AddConstraint(this Property property, Constraint constraint) =>
        property.Domain.CreateMutation().AddConstraint(property, constraint).Apply();

    public static AnalysisResult RemoveConstraint(this Property property, Constraint constraint) =>
        property.Domain.CreateMutation().RemoveConstraint(property, constraint).Apply();

    public static AnalysisResult AddPolicy(this Property property, Policy policy) =>
        property.Domain.CreateMutation().AddPolicy(property, policy).Apply();

    public static AnalysisResult RemovePolicy(this Property property, Policy policy) =>
        property.Domain.CreateMutation().RemovePolicy(property, policy).Apply();

    // ── Event ────────────────────────────────────────────────────────────────

    public static AnalysisResult AddProperty(this Event @event, Property property) =>
        @event.Domain.CreateMutation().AddProperty(@event, property).Apply();

    public static AnalysisResult RemoveProperty(this Event @event, Property property) =>
        @event.Domain.CreateMutation().RemoveProperty(@event, property).Apply();

    // ── Action ───────────────────────────────────────────────────────────────

    public static AnalysisResult AddParameter(this Action action, Property parameter) =>
        action.Domain.CreateMutation().AddParameter(action, parameter).Apply();

    public static AnalysisResult RemoveParameter(this Action action, Property parameter) =>
        action.Domain.CreateMutation().RemoveParameter(action, parameter).Apply();

    public static AnalysisResult AddPolicy(this Action action, Policy policy) =>
        action.Domain.CreateMutation().AddPolicy(action, policy).Apply();

    public static AnalysisResult RemovePolicy(this Action action, Policy policy) =>
        action.Domain.CreateMutation().RemovePolicy(action, policy).Apply();

    public static AnalysisResult AddEffect(this Action action, Effect effect) =>
        action.Domain.CreateMutation().AddEffect(action, effect).Apply();

    public static AnalysisResult RemoveEffect(this Action action, Effect effect) =>
        action.Domain.CreateMutation().RemoveEffect(action, effect).Apply();

    public static AnalysisResult AddEffect(this Effects.Conditional conditional, Effect effect) =>
        conditional.Domain.CreateMutation().AddEffect(conditional, effect).Apply();

    public static AnalysisResult AddEffect(this Composite composite, Effect effect) =>
        composite.Domain.CreateMutation().AddEffect(composite, effect).Apply();

    public static AnalysisResult Produces(this Effect effect, string outputName, DomainType type) =>
        effect.Domain.CreateMutation().SetEffectOutput(effect, outputName, type).Apply();

    public static AnalysisResult BindOutputTo(this Effect sourceEffect, string outputName, Effect targetEffect, string targetParamName) =>
        sourceEffect.Domain.CreateMutation().BindOutputTo(sourceEffect, outputName, targetEffect, targetParamName).Apply();

    public static AnalysisResult BindParameter(this InvokeAction effect, Property targetParameter, DomainValue value) =>
        effect.Domain.CreateMutation().BindParameter(effect, targetParameter, value).Apply();

    public static AnalysisResult BindParameterFrom(this InvokeAction effect, string targetParamName, Effect sourceEffect, string sourceOutputName) =>
        effect.Domain.CreateMutation().BindParameterFrom(effect, targetParamName, sourceEffect, sourceOutputName).Apply();

    public static AnalysisResult SetEventPropertyBinding(this Action action, PublishEvent effect, string propertyName, EventPropertyBindingSource source) =>
        action.Domain.CreateMutation().SetEventPropertyBinding(action, effect, propertyName, source).Apply();

    public static AnalysisResult SetEventHandlerTrigger(this Action action, Event eventType, string eventParameterName) =>
        action.Domain.CreateMutation().SetEventHandlerTrigger(action, eventType, eventParameterName).Apply();

    public static AnalysisResult SetCommandTrigger(this Action action) =>
        action.Domain.CreateMutation().SetCommandTrigger(action).Apply();

    public static AnalysisResult AddEventSubscriptionCorrelation(this EventSubscription subscription, EventCorrelationBinding binding) =>
        subscription.Domain.CreateMutation().AddEventSubscriptionCorrelation(subscription, binding).Apply();

    public static AnalysisResult RemoveEventSubscriptionCorrelation(this EventSubscription subscription, EventCorrelationBinding binding) =>
        subscription.Domain.CreateMutation().RemoveEventSubscriptionCorrelation(subscription, binding).Apply();

    public static AnalysisResult SetAudience(this EventSubscription subscription, EventSubscriptionAudience audience) =>
        subscription.Domain.CreateMutation().SetEventSubscriptionAudience(subscription, audience).Apply();

    public static AnalysisResult SetRoutingMode(this EventSubscription subscription, EventSubscriptionRoutingMode routingMode) =>
        subscription.Domain.CreateMutation().SetEventSubscriptionRoutingMode(subscription, routingMode).Apply();

    public static AnalysisResult SetEventParameter(this EventSubscription subscription, string eventParameterName) =>
        subscription.Domain.CreateMutation().SetEventSubscriptionEventParameter(subscription, eventParameterName).Apply();

    public static AnalysisResult AddEndpoint(this ImportedContract contract, ContractEndpoint endpoint) =>
        contract.Domain.CreateMutation().AddContractEndpoint(contract, endpoint).Apply();

    public static AnalysisResult RemoveEndpoint(this ImportedContract contract, ContractEndpoint endpoint) =>
        contract.Domain.CreateMutation().RemoveContractEndpoint(contract, endpoint).Apply();

    public static AnalysisResult AddFieldMap(this ContractBinding binding, ContractFieldMap map) =>
        binding.Domain.CreateMutation().AddContractFieldMap(binding, map).Apply();

    public static AnalysisResult RemoveFieldMap(this ContractBinding binding, ContractFieldMap map) =>
        binding.Domain.CreateMutation().RemoveContractFieldMap(binding, map).Apply();

    public static AnalysisResult AddComment(this DomainObject target, string comment) =>
        target.Domain.CreateMutation().AddComment(target, comment).Apply();
}