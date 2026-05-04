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

    public static void AddType(this Domain domain, DomainType type) =>
        domain.CreateMutation().AddType(type).Apply();

    public static void AddRelationship(this Domain domain, Relationship relationship) =>
        domain.CreateMutation().AddRelationship(relationship).Apply();

    // ── Entity ───────────────────────────────────────────────────────────────

    public static void AddProperty(this Entity entity, Property property) =>
        entity.Domain.CreateMutation().AddProperty(entity, property).Apply();

    public static void RemoveProperty(this Entity entity, Property property) =>
        entity.Domain.CreateMutation().RemoveProperty(entity, property).Apply();

    public static void AddStage(this Entity entity, Stage stage) =>
        entity.Domain.CreateMutation().AddStage(entity, stage).Apply();

    public static void RemoveStage(this Entity entity, Stage stage) =>
        entity.Domain.CreateMutation().RemoveStage(entity, stage).Apply();

    public static void AddPolicy(this Entity entity, Policy policy) =>
        entity.Domain.CreateMutation().AddPolicy(entity, policy).Apply();

    public static void RemovePolicy(this Entity entity, Policy policy) =>
        entity.Domain.CreateMutation().RemovePolicy(entity, policy).Apply();

    public static void AddAction(this Entity entity, Action action) =>
        entity.Domain.CreateMutation().AddAction(entity, action).Apply();

    public static void RemoveAction(this Entity entity, Action action) =>
        entity.Domain.CreateMutation().RemoveAction(entity, action).Apply();

    public static void AddEvent(this Entity entity, Event @event) =>
        entity.Domain.CreateMutation().AddEvent(entity, @event).Apply();

    public static void RemoveEvent(this Entity entity, Event @event) =>
        entity.Domain.CreateMutation().RemoveEvent(entity, @event).Apply();

    public static void AddRelationship(this Entity entity, Relationship relationship) =>
        entity.Domain.CreateMutation().AddEntityRelationship(entity, relationship).Apply();

    public static void RemoveRelationship(this Entity entity, Relationship relationship) =>
        entity.Domain.CreateMutation().RemoveEntityRelationship(entity, relationship).Apply();

    // ── Actor ────────────────────────────────────────────────────────────────

    public static void SetSubjectProperty(this Actor actor, Property? property) =>
        actor.Domain.CreateMutation().SetActorSubjectProperty(actor, property).Apply();

    public static void SetRoleClaimType(this Actor actor, string? roleClaimType) =>
        actor.Domain.CreateMutation().SetActorRoleClaimType(actor, roleClaimType).Apply();

    public static void AddClaimMapping(this Actor actor, ActorClaimMapping mapping) =>
        actor.Domain.CreateMutation().AddActorClaimMapping(actor, mapping).Apply();

    public static void RemoveClaimMapping(this Actor actor, ActorClaimMapping mapping) =>
        actor.Domain.CreateMutation().RemoveActorClaimMapping(actor, mapping).Apply();

    // ── Stage ────────────────────────────────────────────────────────────────

    public static void AddPolicy(this Stage stage, Policy policy) =>
        stage.Domain.CreateMutation().AddPolicy(stage, policy).Apply();

    public static void RemovePolicy(this Stage stage, Policy policy) =>
        stage.Domain.CreateMutation().RemovePolicy(stage, policy).Apply();

    public static void AddAction(this Stage stage, Action action) =>
        stage.Domain.CreateMutation().AddAction(stage, action).Apply();

    public static void RemoveAction(this Stage stage, Action action) =>
        stage.Domain.CreateMutation().RemoveAction(stage, action).Apply();

    // ── Policy ───────────────────────────────────────────────────────────────

    public static void AddRule(this Policy policy, Rule rule) =>
        policy.Domain.CreateMutation().AddRule(policy, rule).Apply();

    public static void RemoveRule(this Policy policy, Rule rule) =>
        policy.Domain.CreateMutation().RemoveRule(policy, rule).Apply();

    // ── Property ─────────────────────────────────────────────────────────────

    public static void AddConstraint(this Property property, Constraint constraint) =>
        property.Domain.CreateMutation().AddConstraint(property, constraint).Apply();

    public static void RemoveConstraint(this Property property, Constraint constraint) =>
        property.Domain.CreateMutation().RemoveConstraint(property, constraint).Apply();

    public static void AddPolicy(this Property property, Policy policy) =>
        property.Domain.CreateMutation().AddPolicy(property, policy).Apply();

    public static void RemovePolicy(this Property property, Policy policy) =>
        property.Domain.CreateMutation().RemovePolicy(property, policy).Apply();

    // ── Event ────────────────────────────────────────────────────────────────

    public static void AddProperty(this Event @event, Property property) =>
        @event.Domain.CreateMutation().AddProperty(@event, property).Apply();

    public static void RemoveProperty(this Event @event, Property property) =>
        @event.Domain.CreateMutation().RemoveProperty(@event, property).Apply();

    // ── Action ───────────────────────────────────────────────────────────────

    public static void AddParameter(this Action action, Property parameter) =>
        action.Domain.CreateMutation().AddParameter(action, parameter).Apply();

    public static void RemoveParameter(this Action action, Property parameter) =>
        action.Domain.CreateMutation().RemoveParameter(action, parameter).Apply();

    public static void AddPolicy(this Action action, Policy policy) =>
        action.Domain.CreateMutation().AddPolicy(action, policy).Apply();

    public static void RemovePolicy(this Action action, Policy policy) =>
        action.Domain.CreateMutation().RemovePolicy(action, policy).Apply();

    public static bool AddEffect(this Action action, Effect effect) {
        action.Domain.CreateMutation().AddEffect(action, effect).Apply();
        return true;
    }

    public static bool RemoveEffect(this Action action, Effect effect) {
        action.Domain.CreateMutation().RemoveEffect(action, effect).Apply();
        return true;
    }
}