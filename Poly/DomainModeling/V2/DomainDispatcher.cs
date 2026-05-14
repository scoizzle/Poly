using Poly.Data.Modeling;
using Poly.Introspection;

namespace Poly.DomainModeling.V2;

/// <summary>
/// Unified mutation dispatcher that translates high-level <see cref="DomainMutationIntent"/> instances
/// into atomic domain transactions on a <see cref="DomainSession"/>.
/// </summary>
public sealed class DomainDispatcher {
    private readonly DomainSession _session;

    public DomainDispatcher(DomainSession session) {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    /// <summary>The session this dispatcher targets.</summary>
    public DomainSession Session => _session;

    /// <summary>
    /// Dispatches a single intent as an atomic transaction.
    /// </summary>
    public DomainTransactionResult Dispatch(DomainMutationIntent intent) {
        ArgumentNullException.ThrowIfNull(intent);
        return _session.Apply(intent);
    }

    /// <summary>
    /// Dispatches multiple intents as a single atomic transaction.
    /// </summary>
    public DomainTransactionResult Dispatch(IEnumerable<DomainMutationIntent> intents) {
        ArgumentNullException.ThrowIfNull(intents);
        return _session.Apply(intents);
    }

    // ── High-level convenience methods ─────────────────────────────────────────

    public DomainTransactionResult SetDomainName(string name) =>
        Dispatch(new SetDomainNameIntent(name));

    public DomainTransactionResult AddPrimitive(string name, TypeCategory category) =>
        Dispatch(new AddPrimitiveTypeIntent(name, category));

    public DomainTransactionResult AddEntity(string name, string? parentEntityName = null) =>
        Dispatch(new AddEntityTypeIntent(name, parentEntityName is null ? null : new DomainNodeReference(parentEntityName)));

    public DomainTransactionResult AddActor(string name, string? parentEntityName = null) =>
        Dispatch(new AddActorTypeIntent(name, parentEntityName is null ? null : new DomainNodeReference(parentEntityName)));

    public DomainTransactionResult AddEventType(string name) =>
        Dispatch(new AddEventTypeIntent(name));

    public DomainTransactionResult RemoveType(string name) =>
        Dispatch(new RemoveTypeIntent(name));

    public DomainTransactionResult AddPropertyToEntity(string entityName, string propertyName, string typeName) =>
        Dispatch(new AddPropertyToEntityIntent(entityName, propertyName, typeName));

    public DomainTransactionResult RemovePropertyFromEntity(string entityName, string propertyName) =>
        Dispatch(new RemovePropertyFromEntityIntent(entityName, propertyName));

    public DomainTransactionResult AddStageToEntity(string entityName, string stageName, string? parentStageName = null) =>
        Dispatch(new AddStageToEntityIntent(entityName, stageName, parentStageName));

    public DomainTransactionResult RemoveStageFromEntity(string entityName, string stageName) =>
        Dispatch(new RemoveStageFromEntityIntent(entityName, stageName));

    public DomainTransactionResult AddActionToEntity(string entityName, string actionName) =>
        Dispatch(new AddActionToEntityIntent(entityName, actionName));

    public DomainTransactionResult RemoveActionFromEntity(string entityName, string actionName) =>
        Dispatch(new RemoveActionFromEntityIntent(entityName, actionName));

    public DomainTransactionResult AddEventToEntity(string entityName, string eventTypeName) =>
        Dispatch(new AddEventToEntityIntent(entityName, eventTypeName));

    public DomainTransactionResult RemoveEventFromEntity(string entityName, string eventTypeName) =>
        Dispatch(new RemoveEventFromEntityIntent(entityName, eventTypeName));

    public DomainTransactionResult AddPropertyToEventType(string eventTypeName, string propertyName, string typeName) =>
        Dispatch(new AddPropertyToEventTypeIntent(eventTypeName, propertyName, typeName));

    public DomainTransactionResult AddActionToStage(string entityName, string stageName, string actionName) =>
        Dispatch(new AddActionToStageIntent(entityName, stageName, actionName));

    public DomainTransactionResult RemoveActionFromStage(string entityName, string stageName, string actionName) =>
        Dispatch(new RemoveActionFromStageIntent(entityName, stageName, actionName));

    public DomainTransactionResult AddActionParameter(string entityName, string actionName, string parameterName, string typeName) =>
        Dispatch(new AddActionParameterIntent(entityName, actionName, parameterName, typeName));

    public DomainTransactionResult RemoveActionParameter(string entityName, string actionName, string parameterName) =>
        Dispatch(new RemoveActionParameterIntent(entityName, actionName, parameterName));

    public DomainTransactionResult AddRelationship(
        string name,
        string sourceEntityName,
        string targetEntityName,
        RelationshipCardinality cardinality,
        bool sourceOwnsTarget = false) =>
        Dispatch(new AddRelationshipIntent(
            name,
            new DomainNodeReference(sourceEntityName),
            new DomainNodeReference(targetEntityName),
            cardinality,
            sourceOwnsTarget));

    public DomainTransactionResult RemoveRelationship(string name) =>
        Dispatch(new RemoveRelationshipIntent(name));

    public DomainTransactionResult AddComment(string nodePath, string comment) =>
        Dispatch(new AddCommentIntent(nodePath, comment));
}
