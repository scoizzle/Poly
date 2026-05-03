using System.Collections.Concurrent;
using System.ComponentModel;

using ModelContextProtocol.Server;

using Poly.Data.Modeling;
using Poly.Data.Modeling.TypeSystem;
using Poly.Introspection;
using Poly.Syntax.Analysis;

namespace Poly.Mcp;

internal sealed record DomainSessionState(Domain Domain, AnalysisResult? LatestAnalysis, long Revision);

internal static class DomainSessionStore {
    private static readonly ConcurrentDictionary<string, DomainSessionState> Sessions = new(StringComparer.Ordinal);

    public static (string SessionId, DomainSessionState State) Create(string domainName, string? preferredSessionId = null) {
        var sessionId = string.IsNullOrWhiteSpace(preferredSessionId)
            ? Guid.NewGuid().ToString("N")
            : preferredSessionId;

        var state = new DomainSessionState(new Domain(domainName), LatestAnalysis: null, Revision: 0);
        Sessions[sessionId] = state;
        return (sessionId, state);
    }

    public static bool TryGet(string sessionId, out DomainSessionState session) {
        if (string.IsNullOrWhiteSpace(sessionId)) {
            session = null!;
            return false;
        }

        return Sessions.TryGetValue(sessionId, out session!);
    }

    public static long UpdateAnalysis(string sessionId, AnalysisResult analysis) {
        if (string.IsNullOrWhiteSpace(sessionId)) {
            throw new ArgumentException("Session ID is required.", nameof(sessionId));
        }

        if (!Sessions.TryGetValue(sessionId, out var value)) {
            throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        }

        var next = new DomainSessionState(value.Domain, analysis, value.Revision + 1);
        Sessions[sessionId] = next;
        return next.Revision;
    }

    public static IReadOnlyCollection<string> ListSessionIds() => Sessions.Keys.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
}

public sealed record DomainAffordance(
    string Relation,
    string Tool,
    IReadOnlyDictionary<string, object?> Arguments,
    string Description);

public sealed record DomainCommandResponse(
    bool Success,
    string Message,
    string? SessionId,
    string? DomainName,
    long? Revision,
    IReadOnlyCollection<DomainAffordance> Affordances,
    IReadOnlyCollection<string>? Diagnostics = null);

public sealed record DomainCapabilityResponse(
    bool Success,
    string Message,
    string? SessionId,
    long? Revision,
    IReadOnlyCollection<string> AvailableSessions,
    DomainOverviewDto? Overview,
    IReadOnlyCollection<DomainAffordance> Affordances,
    IReadOnlyCollection<string>? Diagnostics = null);

public sealed record DomainQueryResponse<TPayload>(
    bool Success,
    string Message,
    string? SessionId,
    long? Revision,
    TPayload? Data,
    IReadOnlyCollection<DomainAffordance> Affordances,
    IReadOnlyCollection<string>? Diagnostics = null);

public sealed record DomainOverviewDto(
    string DomainName,
    int PrimitiveCount,
    int EntityCount,
    int EventTypeCount,
    int RelationshipCount,
    IReadOnlyCollection<string> PrimitiveNames,
    IReadOnlyCollection<string> EntityNames,
    IReadOnlyCollection<string> EventTypeNames,
    IReadOnlyCollection<string> RelationshipNames);

public sealed record PrimitiveDto(string Name, string Category);

public sealed record PropertyDto(string Name, string TypeName);

public sealed record EntityListItemDto(
    string Name,
    string? ParentEntityName,
    int PropertyCount,
    int EventCount,
    int ActionCount,
    int StageCount,
    int RelationshipCount);

public sealed record EntityActionDto(
    string Name,
    IReadOnlyCollection<string> ParameterNames,
    IReadOnlyCollection<string> EffectTypes,
    IReadOnlyCollection<string> PublishedEvents,
    IReadOnlyCollection<string> TransitionTargets);

public sealed record EntityStageDto(
    string Name,
    IReadOnlyCollection<string> LocalActions,
    IReadOnlyCollection<string> EffectiveActions,
    IReadOnlyCollection<string> LocalPolicies,
    IReadOnlyCollection<string> EffectivePolicies);

public sealed record EntityDetailsDto(
    string Name,
    string? ParentEntityName,
    IReadOnlyCollection<PropertyDto> Properties,
    IReadOnlyCollection<string> Events,
    IReadOnlyCollection<EntityActionDto> Actions,
    IReadOnlyCollection<EntityStageDto> Stages,
    IReadOnlyCollection<string> Relationships);

public sealed record EventTypeListItemDto(string Name, int PropertyCount);

public sealed record EventTypeDetailsDto(string Name, IReadOnlyCollection<PropertyDto> Properties);

public sealed record RelationshipListItemDto(
    string Name,
    string Source,
    string Target,
    string Cardinality,
    bool SourceOwnsTarget);

public sealed record RelationshipDetailsDto(
    string Name,
    string Source,
    string Target,
    string Cardinality,
    bool SourceOwnsTarget,
    IReadOnlyCollection<PropertyDto> Properties,
    IReadOnlyCollection<string> Stages,
    IReadOnlyCollection<string> Policies);

internal static class DomainAffordances {
    public static IReadOnlyCollection<DomainAffordance> SessionRoot() => [
        new("query:overview", nameof(DomainQueryTool.GetDomainOverview), new Dictionary<string, object?>(), "Get domain overview for the active session."),
        new("query:entities", nameof(DomainQueryTool.ListEntities), new Dictionary<string, object?>(), "List entities in the domain."),
        new("query:event-types", nameof(DomainQueryTool.ListEventTypes), new Dictionary<string, object?>(), "List event types in the domain."),
        new("query:relationships", nameof(DomainQueryTool.ListRelationships), new Dictionary<string, object?>(), "List relationships in the domain."),
        new("command:add-entity", nameof(DomainAuthoringTool.AddEntity), new Dictionary<string, object?>(), "Create a new entity."),
        new("command:add-primitive", nameof(DomainAuthoringTool.AddPrimitive), new Dictionary<string, object?>(), "Create a new primitive type."),
        new("command:add-event-type", nameof(DomainAuthoringTool.AddEventType), new Dictionary<string, object?>(), "Create a new event type."),
        new("command:add-relationship", nameof(DomainAuthoringTool.AddRelationship), new Dictionary<string, object?>(), "Create a relationship between two entities.")
    ];

    public static IReadOnlyCollection<DomainAffordance> SessionScoped(string sessionId, params DomainAffordance[] additional) {
        var list = SessionRoot()
            .Select(affordance => affordance with {
                Arguments = new Dictionary<string, object?>(affordance.Arguments, StringComparer.Ordinal) {
                    ["sessionId"] = sessionId
                }
            })
            .ToList();

        foreach (var affordance in additional) {
            list.Add(affordance with {
                Arguments = new Dictionary<string, object?>(affordance.Arguments, StringComparer.Ordinal) {
                    ["sessionId"] = sessionId
                }
            });
        }

        return list;
    }

    public static DomainAffordance GetEntity(string entityName) =>
        new("query:entity", nameof(DomainQueryTool.GetEntity), new Dictionary<string, object?> { ["entityName"] = entityName }, "Get details for a single entity.");

    public static DomainAffordance GetEventType(string eventTypeName) =>
        new("query:event-type", nameof(DomainQueryTool.GetEventType), new Dictionary<string, object?> { ["eventTypeName"] = eventTypeName }, "Get details for a single event type.");

    public static DomainAffordance GetRelationship(string relationshipName) =>
        new("query:relationship", nameof(DomainQueryTool.GetRelationship), new Dictionary<string, object?> { ["relationshipName"] = relationshipName }, "Get details for a single relationship.");
}

[McpServerToolType]
public static class DomainCapabilityTool {
    [McpServerTool, Description("Interrogates Poly domain capabilities and returns available sessions plus an optional lightweight overview for the selected session.")]
    public static DomainCapabilityResponse InterrogateDomainCapabilities(string? sessionId = null) {
        if (string.IsNullOrWhiteSpace(sessionId)) {
            return new DomainCapabilityResponse(
                Success: true,
                Message: "No session selected. Returning available sessions.",
                SessionId: null,
                Revision: null,
                AvailableSessions: DomainSessionStore.ListSessionIds(),
                Overview: null,
                Affordances: [
                    new DomainAffordance("command:create-domain", nameof(DomainAuthoringTool.CreateDomain), new Dictionary<string, object?>(), "Create a new domain session.")
                ]);
        }

        if (!DomainSessionStore.TryGet(sessionId, out var session)) {
            return new DomainCapabilityResponse(
                Success: false,
                Message: $"Session '{sessionId}' was not found.",
                SessionId: sessionId,
                Revision: null,
                AvailableSessions: DomainSessionStore.ListSessionIds(),
                Overview: null,
                Affordances: [],
                Diagnostics: [$"Session '{sessionId}' does not exist."]);
        }

        return new DomainCapabilityResponse(
            Success: true,
            Message: "Domain capability summary generated.",
            SessionId: sessionId,
            Revision: session.Revision,
            AvailableSessions: DomainSessionStore.ListSessionIds(),
            Overview: DomainQueryTool.BuildOverview(session.Domain),
            Affordances: DomainAffordances.SessionScoped(sessionId));
    }
}

[McpServerToolType]
public static class DomainQueryTool {
    [McpServerTool, Description("Returns a lightweight overview of the domain for a session.")]
    public static DomainQueryResponse<DomainOverviewDto> GetDomainOverview(string sessionId) {
        try {
            var session = RequireSession(sessionId);
            return QueryOk(sessionId, session, BuildOverview(session.Domain), "Domain overview returned.");
        }
        catch (Exception ex) { return QueryFail<DomainOverviewDto>(sessionId, ex); }
    }

    [McpServerTool, Description("Lists primitive types in the domain.")]
    public static DomainQueryResponse<IReadOnlyCollection<PrimitiveDto>> ListPrimitives(string sessionId) {
        try {
            var session = RequireSession(sessionId);
            var primitives = session.Domain.GetAvailablePrimitives()
                .OrderBy(static primitive => primitive.Name, StringComparer.Ordinal)
                .Select(static primitive => new PrimitiveDto(primitive.Name, primitive.Category.ToString()))
                .ToArray();
            return QueryOk<IReadOnlyCollection<PrimitiveDto>>(sessionId, session, primitives, "Primitive types returned.");
        }
        catch (Exception ex) { return QueryFail<IReadOnlyCollection<PrimitiveDto>>(sessionId, ex); }
    }

    [McpServerTool, Description("Lists entities with compact statistics. Use GetEntity for detailed shape.")]
    public static DomainQueryResponse<IReadOnlyCollection<EntityListItemDto>> ListEntities(string sessionId) {
        try {
            var session = RequireSession(sessionId);
            var entities = session.Domain.GetAvailableEntities()
                .OrderBy(static entity => entity.Name, StringComparer.Ordinal)
                .Select(static entity => new EntityListItemDto(
                    Name: entity.Name,
                    ParentEntityName: entity.ParentEntity?.Name,
                    PropertyCount: entity.Properties.Count,
                    EventCount: entity.Events.Count,
                    ActionCount: entity.Actions.Count,
                    StageCount: entity.Stages.Count,
                    RelationshipCount: entity.Relationships.Count))
                .ToArray();
            var affordances = entities.Select(static entity => DomainAffordances.GetEntity(entity.Name)).ToArray();
            return QueryOk<IReadOnlyCollection<EntityListItemDto>>(sessionId, session, entities, "Entities returned.", affordances);
        }
        catch (Exception ex) { return QueryFail<IReadOnlyCollection<EntityListItemDto>>(sessionId, ex); }
    }

    [McpServerTool, Description("Returns detailed information for a single entity.")]
    public static DomainQueryResponse<EntityDetailsDto> GetEntity(
        string sessionId,
        string entityName,
        bool includeProperties = true,
        bool includeEvents = true,
        bool includeActions = true,
        bool includeStages = true,
        bool includeRelationships = true) {
        try {
            var session = RequireSession(sessionId);
            var entity = session.Domain.RequireEntity(entityName);
            var analysis = EnsureAnalysis(sessionId, session);

            var data = new EntityDetailsDto(
                Name: entity.Name,
                ParentEntityName: entity.ParentEntity?.Name,
                Properties: includeProperties
                    ? entity.Properties
                        .OrderBy(static property => property.Name, StringComparer.Ordinal)
                        .Select(static property => new PropertyDto(property.Name, property.Type.Name))
                        .ToArray()
                    : [],
                Events: includeEvents
                    ? entity.Events
                        .Select(static @event => @event.Name)
                        .OrderBy(static name => name, StringComparer.Ordinal)
                        .ToArray()
                    : [],
                Actions: includeActions
                    ? entity.Actions
                        .OrderBy(static action => action.Name, StringComparer.Ordinal)
                        .Select(action => analysis.GetCapabilityView(action))
                        .Select(static view => new EntityActionDto(
                            Name: view.ActionName,
                            ParameterNames: view.Parameters.Select(static parameter => parameter.Name).OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
                            EffectTypes: view.EffectTypes.Select(static type => type.Name).OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
                            PublishedEvents: view.PublishedEvents.Select(static @event => @event.Name).OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
                            TransitionTargets: view.TransitionTargets.Select(static stage => stage.Name).OrderBy(static name => name, StringComparer.Ordinal).ToArray()))
                        .ToArray()
                    : [],
                Stages: includeStages
                    ? entity.Stages
                        .OrderBy(static stage => stage.Name, StringComparer.Ordinal)
                        .Select(stage => analysis.GetCapabilityView(stage))
                        .Select(static view => new EntityStageDto(
                            Name: view.StageName,
                            LocalActions: view.LocalActions.Select(static action => action.ActionName).OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
                            EffectiveActions: view.EffectiveActions.Select(static action => action.ActionName).OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
                            LocalPolicies: view.LocalPolicies.Select(static policy => policy.Name).OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
                            EffectivePolicies: view.EffectivePolicies.Select(static policy => policy.Name).OrderBy(static name => name, StringComparer.Ordinal).ToArray()))
                        .ToArray()
                    : [],
                Relationships: includeRelationships
                    ? entity.Relationships.Select(static relationship => relationship.Name).OrderBy(static name => name, StringComparer.Ordinal).ToArray()
                    : []);

            var affordances = DomainAffordances.SessionScoped(
                sessionId,
                new DomainAffordance("command:add-property", nameof(DomainAuthoringTool.AddPropertyToEntity), new Dictionary<string, object?> { ["entityName"] = entityName }, "Add a property to this entity."),
                new DomainAffordance("command:add-stage", nameof(DomainAuthoringTool.AddStageToEntity), new Dictionary<string, object?> { ["entityName"] = entityName }, "Add a lifecycle stage to this entity."),
                new DomainAffordance("command:add-action", nameof(DomainAuthoringTool.AddActionToEntity), new Dictionary<string, object?> { ["entityName"] = entityName }, "Add an action to this entity."),
                new DomainAffordance("command:add-event", nameof(DomainAuthoringTool.AddEventToEntity), new Dictionary<string, object?> { ["entityName"] = entityName }, "Associate an event type with this entity."));

            return QueryOk(sessionId, session, data, $"Entity '{entityName}' returned.", affordances);
        }
        catch (Exception ex) { return QueryFail<EntityDetailsDto>(sessionId, ex); }
    }

    [McpServerTool, Description("Lists event types with compact statistics. Use GetEventType for details.")]
    public static DomainQueryResponse<IReadOnlyCollection<EventTypeListItemDto>> ListEventTypes(string sessionId) {
        try {
            var session = RequireSession(sessionId);
            var eventTypes = session.Domain.GetAvailableEventTypes()
                .OrderBy(static @event => @event.Name, StringComparer.Ordinal)
                .Select(static @event => new EventTypeListItemDto(@event.Name, @event.Properties.Count))
                .ToArray();
            var affordances = eventTypes.Select(static @event => DomainAffordances.GetEventType(@event.Name)).ToArray();
            return QueryOk<IReadOnlyCollection<EventTypeListItemDto>>(sessionId, session, eventTypes, "Event types returned.", affordances);
        }
        catch (Exception ex) { return QueryFail<IReadOnlyCollection<EventTypeListItemDto>>(sessionId, ex); }
    }

    [McpServerTool, Description("Returns detailed information for a single event type.")]
    public static DomainQueryResponse<EventTypeDetailsDto> GetEventType(string sessionId, string eventTypeName) {
        try {
            var session = RequireSession(sessionId);
            var eventType = session.Domain.RequireEventType(eventTypeName);
            var data = new EventTypeDetailsDto(
                Name: eventType.Name,
                Properties: eventType.Properties
                    .OrderBy(static property => property.Name, StringComparer.Ordinal)
                    .Select(static property => new PropertyDto(property.Name, property.Type.Name))
                    .ToArray());

            var affordances = DomainAffordances.SessionScoped(
                sessionId,
                new DomainAffordance("command:add-property", nameof(DomainAuthoringTool.AddPropertyToEventType), new Dictionary<string, object?> { ["eventTypeName"] = eventTypeName }, "Add a property to this event type."));

            return QueryOk(sessionId, session, data, $"Event type '{eventTypeName}' returned.", affordances);
        }
        catch (Exception ex) { return QueryFail<EventTypeDetailsDto>(sessionId, ex); }
    }

    [McpServerTool, Description("Lists relationships in the domain. Use GetRelationship for details.")]
    public static DomainQueryResponse<IReadOnlyCollection<RelationshipListItemDto>> ListRelationships(string sessionId) {
        try {
            var session = RequireSession(sessionId);
            var relationships = session.Domain.GetAvailableRelationships()
                .OrderBy(static relationship => relationship.Name, StringComparer.Ordinal)
                .Select(static relationship => new RelationshipListItemDto(
                    Name: relationship.Name,
                    Source: relationship.Source.Name,
                    Target: relationship.Target.Name,
                    Cardinality: relationship.Cardinality.ToString(),
                    SourceOwnsTarget: relationship.SourceOwnsTarget))
                .ToArray();
            var affordances = relationships.Select(static relationship => DomainAffordances.GetRelationship(relationship.Name)).ToArray();
            return QueryOk<IReadOnlyCollection<RelationshipListItemDto>>(sessionId, session, relationships, "Relationships returned.", affordances);
        }
        catch (Exception ex) { return QueryFail<IReadOnlyCollection<RelationshipListItemDto>>(sessionId, ex); }
    }

    [McpServerTool, Description("Returns detailed information for a single relationship.")]
    public static DomainQueryResponse<RelationshipDetailsDto> GetRelationship(string sessionId, string relationshipName) {
        try {
            var session = RequireSession(sessionId);
            var relationship = session.Domain.GetAvailableRelationships()
                .FirstOrDefault(r => string.Equals(r.Name, relationshipName, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Relationship '{relationshipName}' was not found in domain '{session.Domain.Name}'.");

            var data = new RelationshipDetailsDto(
                Name: relationship.Name,
                Source: relationship.Source.Name,
                Target: relationship.Target.Name,
                Cardinality: relationship.Cardinality.ToString(),
                SourceOwnsTarget: relationship.SourceOwnsTarget,
                Properties: relationship.Properties
                    .OrderBy(static property => property.Name, StringComparer.Ordinal)
                    .Select(static property => new PropertyDto(property.Name, property.Type.Name))
                    .ToArray(),
                Stages: relationship.Stages
                    .Select(static stage => stage.Name)
                    .OrderBy(static name => name, StringComparer.Ordinal)
                    .ToArray(),
                Policies: relationship.Policies
                    .Select(static policy => policy.Name)
                    .OrderBy(static name => name, StringComparer.Ordinal)
                    .ToArray());

            return QueryOk(sessionId, session, data, $"Relationship '{relationshipName}' returned.");
        }
        catch (Exception ex) { return QueryFail<RelationshipDetailsDto>(sessionId, ex); }
    }

    internal static DomainOverviewDto BuildOverview(Domain domain) {
        var primitiveNames = domain.GetAvailablePrimitives().Select(static primitive => primitive.Name).OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        var entityNames = domain.GetAvailableEntities().Select(static entity => entity.Name).OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        var eventTypeNames = domain.GetAvailableEventTypes().Select(static @event => @event.Name).OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        var relationshipNames = domain.GetAvailableRelationships().Select(static relationship => relationship.Name).OrderBy(static name => name, StringComparer.Ordinal).ToArray();

        return new DomainOverviewDto(
            DomainName: domain.Name,
            PrimitiveCount: primitiveNames.Length,
            EntityCount: entityNames.Length,
            EventTypeCount: eventTypeNames.Length,
            RelationshipCount: relationshipNames.Length,
            PrimitiveNames: primitiveNames,
            EntityNames: entityNames,
            EventTypeNames: eventTypeNames,
            RelationshipNames: relationshipNames);
    }

    private static DomainSessionState RequireSession(string sessionId) {
        if (!DomainSessionStore.TryGet(sessionId, out var session)) {
            throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        }

        return session;
    }

    private static AnalysisResult EnsureAnalysis(string sessionId, DomainSessionState session) {
        if (session.LatestAnalysis is not null) {
            return session.LatestAnalysis;
        }

        var analysis = new DomainModelAnalyzer().Analyze(session.Domain);
        _ = DomainSessionStore.UpdateAnalysis(sessionId, analysis);

        if (!DomainSessionStore.TryGet(sessionId, out var updated)) {
            throw new InvalidOperationException($"Session '{sessionId}' was not found after analysis update.");
        }

        return updated.LatestAnalysis!;
    }

    private static DomainQueryResponse<TPayload> QueryOk<TPayload>(
        string sessionId,
        DomainSessionState session,
        TPayload data,
        string message,
        IReadOnlyCollection<DomainAffordance>? affordances = null) =>
        new(
            Success: true,
            Message: message,
            SessionId: sessionId,
            Revision: session.Revision,
            Data: data,
            Affordances: affordances ?? DomainAffordances.SessionScoped(sessionId));

    private static DomainQueryResponse<TPayload> QueryFail<TPayload>(string? sessionId, Exception ex) =>
        new(
            Success: false,
            Message: ex.Message,
            SessionId: sessionId,
            Revision: null,
            Data: default,
            Affordances: [],
            Diagnostics: [ex.ToString()]);
}

[McpServerToolType]
public static class DomainAuthoringTool {
    [McpServerTool, Description("Creates a new Poly domain session with the given name. Returns the session ID for subsequent commands and queries.")]
    public static DomainCommandResponse CreateDomain(
        [Description("The name of the domain to create.")] string domainName,
        [Description("Optional preferred session ID. A unique ID is generated if omitted.")] string? sessionId = null) {
        try {
            var (id, state) = DomainSessionStore.Create(domainName, sessionId);
            return Ok(id, state.Domain.Name, state.Revision, $"Created domain '{domainName}' in session '{id}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Renames the domain in an existing session.")]
    public static DomainCommandResponse SetDomainName(
        [Description("The session ID.")] string sessionId,
        [Description("The new domain name.")] string name) {
        try {
            var state = RequireSession(sessionId);
            var analysis = state.Domain.CreateMutation().SetDomainName(name).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Domain renamed to '{name}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Adds a new entity type to the domain.")]
    public static DomainCommandResponse AddEntity(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the new entity.")] string name,
        [Description("Name of the parent entity to inherit from, if any.")] string? parentEntityName = null) {
        try {
            var state = RequireSession(sessionId);
            var parent = string.IsNullOrWhiteSpace(parentEntityName) ? null : state.Domain.RequireEntity(parentEntityName);
            var analysis = state.Domain.CreateMutation().AddType(new Entity(state.Domain, name, parent)).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Entity '{name}' added.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Adds a primitive type to the domain.")]
    public static DomainCommandResponse AddPrimitive(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the new primitive type.")] string name,
        [Description("Type category: Primitive, Enum, or Value. Defaults to Primitive.")] string? category = null) {
        try {
            var state = RequireSession(sessionId);
            var typeCategory = ParseTypeCategory(category);
            var analysis = state.Domain.CreateMutation().AddType(new Primitive(state.Domain, name, typeCategory)).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Primitive '{name}' ({typeCategory}) added.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Adds an event type to the domain.")]
    public static DomainCommandResponse AddEventType(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the new event type.")] string name) {
        try {
            var state = RequireSession(sessionId);
            var analysis = state.Domain.CreateMutation().AddType(new Event(state.Domain, name)).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Event type '{name}' added.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Adds a typed property to an entity.")]
    public static DomainCommandResponse AddPropertyToEntity(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the target entity.")] string entityName,
        [Description("Name of the new property.")] string propertyName,
        [Description("Name of the domain type for this property.")] string typeName) {
        try {
            var state = RequireSession(sessionId);
            var entity = state.Domain.RequireEntity(entityName);
            var type = ResolveType(state.Domain, typeName);
            var analysis = state.Domain.CreateMutation().AddProperty(entity, new Property(state.Domain, propertyName, type)).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Property '{propertyName}' added to entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Adds a typed property to an event type.")]
    public static DomainCommandResponse AddPropertyToEventType(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the target event type.")] string eventTypeName,
        [Description("Name of the new property.")] string propertyName,
        [Description("Name of the domain type for this property.")] string typeName) {
        try {
            var state = RequireSession(sessionId);
            var eventType = state.Domain.RequireEventType(eventTypeName);
            var type = ResolveType(state.Domain, typeName);
            var analysis = state.Domain.CreateMutation().AddProperty(eventType, new Property(state.Domain, propertyName, type)).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Property '{propertyName}' added to event type '{eventTypeName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Adds a lifecycle stage to an entity.")]
    public static DomainCommandResponse AddStageToEntity(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the target entity.")] string entityName,
        [Description("Name of the new stage.")] string stageName,
        [Description("Name of an existing stage on this entity to use as the parent stage, if any.")] string? parentStageName = null) {
        try {
            var state = RequireSession(sessionId);
            var entity = state.Domain.RequireEntity(entityName);
            var parent = string.IsNullOrWhiteSpace(parentStageName) ? null : entity.RequireStage(parentStageName);
            var stage = new Stage(state.Domain, stageName) { Parent = parent };
            var analysis = state.Domain.CreateMutation().AddStage(entity, stage).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Stage '{stageName}' added to entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Adds an action to an entity.")]
    public static DomainCommandResponse AddActionToEntity(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the target entity.")] string entityName,
        [Description("Name of the new action.")] string actionName) {
        try {
            var state = RequireSession(sessionId);
            var entity = state.Domain.RequireEntity(entityName);
            var action = new Data.Modeling.Action(state.Domain, actionName, entity);
            var analysis = state.Domain.CreateMutation().AddAction(entity, action).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Action '{actionName}' added to entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Associates an event type with an entity.")]
    public static DomainCommandResponse AddEventToEntity(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the target entity.")] string entityName,
        [Description("Name of the event type to associate.")] string eventTypeName) {
        try {
            var state = RequireSession(sessionId);
            var entity = state.Domain.RequireEntity(entityName);
            var eventType = state.Domain.RequireEventType(eventTypeName);
            var analysis = state.Domain.CreateMutation().AddEvent(entity, eventType).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Event '{eventTypeName}' associated with entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Defines a typed relationship between two entities.")]
    public static DomainCommandResponse AddRelationship(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the relationship.")] string name,
        [Description("Name of the source entity.")] string sourceEntityName,
        [Description("Name of the target entity.")] string targetEntityName,
        [Description("Cardinality: OneToOne, OneToMany, ManyToOne, or ManyToMany. Defaults to OneToMany.")] string cardinality = nameof(RelationshipCardinality.OneToMany),
        [Description("Whether the source entity owns and controls the lifecycle of target instances.")] bool sourceOwnsTarget = false) {
        try {
            var state = RequireSession(sessionId);
            var source = state.Domain.RequireEntity(sourceEntityName);
            var target = state.Domain.RequireEntity(targetEntityName);
            var relationship = new Relationship(state.Domain, name, source, target, ParseCardinality(cardinality), sourceOwnsTarget);

            var analysis = state.Domain.CreateMutation()
                .AddRelationship(relationship)
                .AddEntityRelationship(source, relationship)
                .Apply(state.LatestAnalysis);

            return Commit(sessionId, state.Domain, analysis, $"Relationship '{name}' ({sourceEntityName} -> {targetEntityName}) added.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    private static DomainSessionState RequireSession(string sessionId) {
        if (!DomainSessionStore.TryGet(sessionId, out var session)) {
            throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        }

        return session;
    }

    private static DomainCommandResponse Commit(string sessionId, Domain domain, AnalysisResult analysis, string message) {
        var revision = DomainSessionStore.UpdateAnalysis(sessionId, analysis);
        var diagnostics = analysis.Diagnostics
            .Select(static diagnostic => $"{diagnostic.Severity}: {diagnostic.Code} - {diagnostic.Message}")
            .ToArray();

        return new DomainCommandResponse(
            Success: true,
            Message: message,
            SessionId: sessionId,
            DomainName: domain.Name,
            Revision: revision,
            Affordances: DomainAffordances.SessionScoped(sessionId),
            Diagnostics: diagnostics);
    }

    private static DomainCommandResponse Ok(string sessionId, string domainName, long revision, string message) =>
        new(
            Success: true,
            Message: message,
            SessionId: sessionId,
            DomainName: domainName,
            Revision: revision,
            Affordances: DomainAffordances.SessionScoped(sessionId));

    private static DomainCommandResponse Fail(string? sessionId, Exception ex) =>
        new(
            Success: false,
            Message: ex.Message,
            SessionId: sessionId,
            DomainName: null,
            Revision: null,
            Affordances: [],
            Diagnostics: [ex.ToString()]);

    private static RelationshipCardinality ParseCardinality(string? value) {
        var input = string.IsNullOrWhiteSpace(value) ? nameof(RelationshipCardinality.OneToMany) : value;
        if (Enum.TryParse<RelationshipCardinality>(input, ignoreCase: true, out var parsed)) {
            return parsed;
        }

        throw new InvalidOperationException($"Unknown relationship cardinality '{value}'.");
    }

    private static TypeCategory ParseTypeCategory(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return TypeCategory.Primitive;
        }

        if (Enum.TryParse<TypeCategory>(value, ignoreCase: true, out var parsed)) {
            return parsed;
        }

        throw new InvalidOperationException($"Unknown type category '{value}'.");
    }

    private static DomainType ResolveType(Domain domain, string typeName) {
        foreach (var type in domain.Types) {
            if (string.Equals(type.Name, typeName, StringComparison.Ordinal)) {
                return type;
            }
        }

        throw new InvalidOperationException($"Type '{typeName}' was not found in domain '{domain.Name}'.");
    }
}