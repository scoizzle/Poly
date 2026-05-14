using Poly.Data.Modeling;
using Poly.Data.Modeling.TypeSystem;
using Poly.Introspection;

namespace Poly.DomainModeling.V2;

/// <summary>
/// High-level intent tools — fluent extension methods on <see cref="DomainSession"/> for
/// the most common authoring operations without constructing intent records manually.
/// </summary>
public static class DomainSessionIntentExtensions {
    // ── Domain ─────────────────────────────────────────────────────────────────

    public static DomainTransactionResult SetName(this DomainSession session, string name) =>
        session.Apply(new SetDomainNameIntent(name));

    // ── Types ──────────────────────────────────────────────────────────────────

    public static DomainTransactionResult AddPrimitive(this DomainSession session, string name, TypeCategory category) =>
        session.Apply(new AddPrimitiveTypeIntent(name, category));

    public static DomainTransactionResult AddEntity(this DomainSession session, string name, string? parentEntityName = null) =>
        session.Apply(new AddEntityTypeIntent(name, parentEntityName is null ? null : new DomainNodeReference(parentEntityName)));

    public static DomainTransactionResult AddActor(this DomainSession session, string name, string? parentEntityName = null) =>
        session.Apply(new AddActorTypeIntent(name, parentEntityName is null ? null : new DomainNodeReference(parentEntityName)));

    public static DomainTransactionResult AddEventType(this DomainSession session, string name) =>
        session.Apply(new AddEventTypeIntent(name));

    public static DomainTransactionResult RemoveType(this DomainSession session, string name) =>
        session.Apply(new RemoveTypeIntent(name));

    // ── Properties ─────────────────────────────────────────────────────────────

    public static DomainTransactionResult AddProperty(this DomainSession session, string entityName, string propertyName, string typeName) =>
        session.Apply(new AddPropertyToEntityIntent(entityName, propertyName, typeName));

    public static DomainTransactionResult RemoveProperty(this DomainSession session, string entityName, string propertyName) =>
        session.Apply(new RemovePropertyFromEntityIntent(entityName, propertyName));

    // ── Stages ─────────────────────────────────────────────────────────────────

    public static DomainTransactionResult AddStage(this DomainSession session, string entityName, string stageName, string? parentStageName = null) =>
        session.Apply(new AddStageToEntityIntent(entityName, stageName, parentStageName));

    public static DomainTransactionResult RemoveStage(this DomainSession session, string entityName, string stageName) =>
        session.Apply(new RemoveStageFromEntityIntent(entityName, stageName));

    // ── Actions ────────────────────────────────────────────────────────────────

    public static DomainTransactionResult AddAction(this DomainSession session, string entityName, string actionName) =>
        session.Apply(new AddActionToEntityIntent(entityName, actionName));

    public static DomainTransactionResult RemoveAction(this DomainSession session, string entityName, string actionName) =>
        session.Apply(new RemoveActionFromEntityIntent(entityName, actionName));

    public static DomainTransactionResult AddActionToStage(this DomainSession session, string entityName, string stageName, string actionName) =>
        session.Apply(new AddActionToStageIntent(entityName, stageName, actionName));

    public static DomainTransactionResult RemoveActionFromStage(this DomainSession session, string entityName, string stageName, string actionName) =>
        session.Apply(new RemoveActionFromStageIntent(entityName, stageName, actionName));

    public static DomainTransactionResult AddActionParameter(this DomainSession session, string entityName, string actionName, string paramName, string typeName) =>
        session.Apply(new AddActionParameterIntent(entityName, actionName, paramName, typeName));

    public static DomainTransactionResult RemoveActionParameter(this DomainSession session, string entityName, string actionName, string paramName) =>
        session.Apply(new RemoveActionParameterIntent(entityName, actionName, paramName));

    // ── Events ─────────────────────────────────────────────────────────────────

    public static DomainTransactionResult AddEventToEntity(this DomainSession session, string entityName, string eventTypeName) =>
        session.Apply(new AddEventToEntityIntent(entityName, eventTypeName));

    public static DomainTransactionResult RemoveEventFromEntity(this DomainSession session, string entityName, string eventTypeName) =>
        session.Apply(new RemoveEventFromEntityIntent(entityName, eventTypeName));

    public static DomainTransactionResult AddPropertyToEventType(this DomainSession session, string eventTypeName, string propertyName, string typeName) =>
        session.Apply(new AddPropertyToEventTypeIntent(eventTypeName, propertyName, typeName));

    // ── Relationships ──────────────────────────────────────────────────────────

    public static DomainTransactionResult AddRelationship(
        this DomainSession session,
        string name,
        string sourceEntityName,
        string targetEntityName,
        RelationshipCardinality cardinality,
        bool sourceOwnsTarget = false) =>
        session.Apply(new AddRelationshipIntent(
            name,
            new DomainNodeReference(sourceEntityName),
            new DomainNodeReference(targetEntityName),
            cardinality,
            sourceOwnsTarget));

    public static DomainTransactionResult RemoveRelationship(this DomainSession session, string name) =>
        session.Apply(new RemoveRelationshipIntent(name));

    // ── Comments ───────────────────────────────────────────────────────────────

    public static DomainTransactionResult AddComment(this DomainSession session, string nodePath, string comment) =>
        session.Apply(new AddCommentIntent(nodePath, comment));
}
