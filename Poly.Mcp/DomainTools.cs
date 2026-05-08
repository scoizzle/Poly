using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;

using ModelContextProtocol.Server;

using Poly.Data.Modeling;
using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Introspection;
using Poly.Syntax;
using Poly.Syntax.Analysis;

namespace Poly.Mcp;

internal sealed record DomainSessionState(
    Domain Domain,
    AnalysisResult? LatestAnalysis,
    long Revision,
    IReadOnlyDictionary<long, DomainSnapshot> RevisionSnapshots);

internal static class DomainSessionStore {
    private const int MaxRevisionSnapshots = 64;
    private static readonly ConcurrentDictionary<string, DomainSessionState> Sessions = new(StringComparer.Ordinal);

    public static (string SessionId, DomainSessionState State) Create(string domainName, string? preferredSessionId = null) {
        var sessionId = string.IsNullOrWhiteSpace(preferredSessionId)
            ? Guid.NewGuid().ToString("N")
            : preferredSessionId;

        var domain = new Domain(domainName);
        var bootstrap = domain.CreateMutation();
        CanonicalBuiltInTypeCatalog.AddToMutation(bootstrap);
        var initialAnalysis = bootstrap.Apply();
        var snapshots = new Dictionary<long, DomainSnapshot> {
            [0] = DomainDiffUtil.CaptureSnapshot(domain)
        };
        var state = new DomainSessionState(domain, LatestAnalysis: initialAnalysis, Revision: 0, RevisionSnapshots: snapshots);
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

        var nextRevision = value.Revision + 1;
        var snapshots = new Dictionary<long, DomainSnapshot>(value.RevisionSnapshots) {
            [nextRevision] = DomainDiffUtil.CaptureSnapshot(value.Domain)
        };

        if (snapshots.Count > MaxRevisionSnapshots) {
            foreach (var revision in snapshots.Keys.OrderBy(static revision => revision).Take(snapshots.Count - MaxRevisionSnapshots).ToArray()) {
                snapshots.Remove(revision);
            }
        }

        var next = new DomainSessionState(value.Domain, analysis, nextRevision, snapshots);
        Sessions[sessionId] = next;
        return next.Revision;
    }

    public static bool TryGetRevisionSnapshot(string sessionId, long revision, out DomainSnapshot snapshot) {
        if (!TryGet(sessionId, out var session) || !session.RevisionSnapshots.TryGetValue(revision, out snapshot!)) {
            snapshot = null!;
            return false;
        }

        return true;
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

public sealed record PrimitiveDto(string Name, string Category, bool IsRequired, bool IsNullable);

public sealed record EnumMemberDto(string Name, object? CanonicalValue = null, string? Label = null);

public sealed record ConstraintTypeDto(string TypeName, string DisplayName, string[] ParameterNames);

public sealed record ConstraintDto(
    string Kind,
    object? Value = null,
    object? MinValue = null,
    object? MaxValue = null,
    int? MinLength = null,
    int? MaxLength = null,
    IReadOnlyCollection<EnumMemberDto>? EnumMembers = null);

public sealed record EventPropertyBindingExportDto(string EventTypeName, string PropertyName, string SourceKind, string SourceName);

public sealed record ActionExportDto(
    string Name,
    IReadOnlyCollection<PropertyExportDto> Parameters,
    IReadOnlyCollection<EventPropertyBindingExportDto> PublishEventBindings);

public sealed record PropertyExportDto(string Name, string TypeName, IReadOnlyCollection<ConstraintDto> Constraints);

public sealed record PrimitiveExportDto(string Name, string Category, IReadOnlyCollection<ConstraintDto> Constraints);

public sealed record EntityExportDto(
    string Name,
    bool IsActor,
    string? ParentEntityName,
    IReadOnlyCollection<ConstraintDto> Constraints,
    IReadOnlyCollection<PropertyExportDto> Properties,
    IReadOnlyCollection<ActionExportDto>? Actions = null);

public sealed record EventTypeExportDto(string Name, IReadOnlyCollection<PropertyExportDto> Properties);

public sealed record RelationshipExportDto(
    string Name,
    string SourceEntityName,
    string TargetEntityName,
    string Cardinality,
    bool SourceOwnsTarget);

public sealed record DomainSessionExportDto(
    string DomainName,
    IReadOnlyCollection<PrimitiveExportDto> Primitives,
    IReadOnlyCollection<EntityExportDto> Entities,
    IReadOnlyCollection<EventTypeExportDto> EventTypes,
    IReadOnlyCollection<RelationshipExportDto> Relationships);

public sealed record PropertyDto(
    string Name,
    string TypeName,
    IReadOnlyCollection<EnumMemberDto> LocalEnumMembers,
    IReadOnlyCollection<EnumMemberDto> EffectiveEnumMembers);

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

public sealed record DomainHealthDto(
    bool HasErrors,
    int ErrorCount,
    int WarningCount,
    TimeSpan TotalAnalysisTime,
    bool Incremental,
    IReadOnlyCollection<AnalyzerPassTelemetry> Passes);

public sealed record DomainInvalidityDto(
    int ErrorCount,
    int WarningCount,
    IReadOnlyCollection<NodeInvalidityNodeReport> Nodes);

public sealed record DomainRevisionDiffDto(
    long FromRevision,
    long ToRevision,
    int AddedCount,
    int RemovedCount,
    int ChangedCount,
    IReadOnlyCollection<DomainNodeSnapshot> Added,
    IReadOnlyCollection<DomainNodeSnapshot> Removed,
    IReadOnlyCollection<DomainNodeChange> Changed);

public sealed record MutationTraceDto(
    bool Succeeded,
    bool RolledBack,
    int AppliedStepCount,
    TimeSpan Duration,
    int ErrorCount,
    int WarningCount,
    IReadOnlyCollection<NodeId> AffectedNodeIds,
    IReadOnlyCollection<DomainMutationStepTrace> Steps,
    IReadOnlyCollection<string> Diagnostics);

internal static class DomainAffordances {
    public static IReadOnlyCollection<DomainAffordance> SessionRoot() => [
        new("query:overview", nameof(DomainQueryTool.GetDomainOverview), new Dictionary<string, object?>(), "Get domain overview for the active session."),
        new("query:health", nameof(DomainOperabilityTool.GetDomainHealth), new Dictionary<string, object?>(), "Get analyzer telemetry and diagnostic summary for the domain."),
        new("query:invalidity", nameof(DomainOperabilityTool.ExplainInvalidDomain), new Dictionary<string, object?>(), "Get grouped invalidity reasons and hints by node."),
        new("query:revision-diff", nameof(DomainOperabilityTool.DiffDomainRevision), new Dictionary<string, object?>(), "Compare two stored domain revisions."),
        new("query:entities", nameof(DomainQueryTool.ListEntities), new Dictionary<string, object?>(), "List entities in the domain."),
        new("query:event-types", nameof(DomainQueryTool.ListEventTypes), new Dictionary<string, object?>(), "List event types in the domain."),
        new("query:relationships", nameof(DomainQueryTool.ListRelationships), new Dictionary<string, object?>(), "List relationships in the domain."),
        new("command:add-entity", nameof(DomainAuthoringTool.AddEntity), new Dictionary<string, object?>(), "Create a new entity."),
        new("command:add-actor", nameof(DomainAuthoringTool.AddActor), new Dictionary<string, object?>(), "Create a new actor entity."),
        new("command:set-actor-subject-property", nameof(DomainAuthoringTool.SetActorSubjectProperty), new Dictionary<string, object?>(), "Set the actor property that maps to the principal subject ID."),
        new("command:set-actor-role-claim-type", nameof(DomainAuthoringTool.SetActorRoleClaimType), new Dictionary<string, object?>(), "Set the claim type carrying role values for an actor."),
        new("command:add-actor-claim-mapping", nameof(DomainAuthoringTool.AddActorClaimMapping), new Dictionary<string, object?>(), "Bind a principal claim type to an actor property."),
        new("command:remove-actor-claim-mapping", nameof(DomainAuthoringTool.RemoveActorClaimMapping), new Dictionary<string, object?>(), "Remove a claim-to-property mapping from an actor."),
        new("command:add-policy-to-entity", nameof(DomainAuthoringTool.AddPolicyToEntity), new Dictionary<string, object?>(), "Create a policy on an entity."),
        new("command:remove-policy-from-entity", nameof(DomainAuthoringTool.RemovePolicyFromEntity), new Dictionary<string, object?>(), "Remove a policy from an entity."),
        new("command:add-policy-to-stage", nameof(DomainAuthoringTool.AddPolicyToStage), new Dictionary<string, object?>(), "Create a policy on a stage."),
        new("command:remove-policy-from-stage", nameof(DomainAuthoringTool.RemovePolicyFromStage), new Dictionary<string, object?>(), "Remove a policy from a stage."),
        new("command:add-policy-to-property", nameof(DomainAuthoringTool.AddPolicyToProperty), new Dictionary<string, object?>(), "Create a policy on a property."),
        new("command:remove-policy-from-property", nameof(DomainAuthoringTool.RemovePolicyFromProperty), new Dictionary<string, object?>(), "Remove a policy from a property."),
        new("command:add-policy-to-action", nameof(DomainAuthoringTool.AddPolicyToAction), new Dictionary<string, object?>(), "Create a policy on an action."),
        new("command:remove-policy-from-action", nameof(DomainAuthoringTool.RemovePolicyFromAction), new Dictionary<string, object?>(), "Remove a policy from an action."),
        new("command:add-cross-property-rule", nameof(DomainAuthoringTool.AddCrossPropertyRuleToPolicy), new Dictionary<string, object?> { ["sessionId"] = "", ["entityName"] = "", ["policyName"] = "", ["ruleName"] = "", ["leftPropertyName"] = "", ["rightPropertyName"] = "", ["operator"] = "", ["stageName"] = null, ["actionName"] = null, ["propertyName"] = null }, "Add a cross-property comparison rule to a policy."),
        new("command:add-actor-type-rule", nameof(DomainAuthoringTool.AddActorTypeRuleToPolicy), new Dictionary<string, object?> { ["sessionId"] = "", ["entityName"] = "", ["policyName"] = "", ["ruleName"] = "", ["actorTypeName"] = "", ["stageName"] = null, ["actionName"] = null, ["propertyName"] = null }, "Require the actor to be a specific actor type."),
        new("command:add-actor-role-rule", nameof(DomainAuthoringTool.AddActorRoleRuleToPolicy), new Dictionary<string, object?> { ["sessionId"] = "", ["entityName"] = "", ["policyName"] = "", ["ruleName"] = "", ["role"] = "", ["stageName"] = null, ["actionName"] = null, ["propertyName"] = null }, "Require the actor to have a specific role."),
        new("command:add-actor-property-rule", nameof(DomainAuthoringTool.AddActorPropertyRuleToPolicy), new Dictionary<string, object?> { ["sessionId"] = "", ["entityName"] = "", ["policyName"] = "", ["ruleName"] = "", ["actorTypeName"] = "", ["actorPropertyName"] = "", ["constraintValue"] = "", ["stageName"] = null, ["actionName"] = null, ["propertyName"] = null }, "Require an actor property to equal a specific value."),
        new("command:add-composite-rule", nameof(DomainAuthoringTool.AddCompositeRuleToPolicy), new Dictionary<string, object?> { ["sessionId"] = "", ["entityName"] = "", ["policyName"] = "", ["ruleName"] = "", ["leftRuleName"] = "", ["rightRuleName"] = "", ["operator"] = "", ["stageName"] = null, ["actionName"] = null, ["propertyName"] = null }, "Combine two existing rules with And/Or."),
        new("command:remove-rule-from-policy", nameof(DomainAuthoringTool.RemoveRuleFromPolicy), new Dictionary<string, object?> { ["sessionId"] = "", ["entityName"] = "", ["policyName"] = "", ["ruleName"] = "", ["stageName"] = null, ["actionName"] = null, ["propertyName"] = null }, "Remove a rule from a policy."),
        new("command:add-primitive", nameof(DomainAuthoringTool.AddPrimitive), new Dictionary<string, object?>(), "Create a new primitive type."),
        new("command:add-enum-constraint-to-type", nameof(DomainAuthoringTool.AddEnumConstraintToType), new Dictionary<string, object?>(), "Attach a closed enum constraint to a domain type."),
        new("command:add-enum-constraint-to-entity-property", nameof(DomainAuthoringTool.AddEnumConstraintToEntityProperty), new Dictionary<string, object?>(), "Attach a closed enum constraint to an entity property."),
        new("command:get-available-constraint-types", nameof(DomainAuthoringTool.GetAvailableConstraintTypes), new Dictionary<string, object?> { ["sessionId"] = "", ["typeName"] = null, ["entityName"] = null, ["propertyName"] = null }, "Returns constraint types applicable to a domain type or entity property."),
        new("command:add-constraint-to-type", nameof(DomainAuthoringTool.AddConstraintToType), new Dictionary<string, object?> { ["sessionId"] = "", ["typeName"] = "", ["constraintType"] = "", ["minLength"] = null, ["maxLength"] = null, ["minValue"] = null, ["maxValue"] = null, ["members"] = null, ["value"] = null }, "Add or replace a constraint on a domain type by constraint type name."),
        new("command:add-constraint-to-entity-property", nameof(DomainAuthoringTool.AddConstraintToEntityProperty), new Dictionary<string, object?> { ["sessionId"] = "", ["entityName"] = "", ["propertyName"] = "", ["constraintType"] = "", ["minLength"] = null, ["maxLength"] = null, ["minValue"] = null, ["maxValue"] = null, ["members"] = null, ["value"] = null }, "Add or replace a constraint on an entity property by constraint type name."),
        new("command:add-event-type", nameof(DomainAuthoringTool.AddEventType), new Dictionary<string, object?> { ["sessionId"] = "", ["name"] = "" }, "Create a new event type."),
        new("command:add-relationship", nameof(DomainAuthoringTool.AddRelationship), new Dictionary<string, object?>(), "Create a relationship between two entities."),
        new("command:apply-mutation-with-trace", nameof(DomainOperabilityTool.ApplyMutationWithTrace), new Dictionary<string, object?>(), "Apply a basic mutation and return detailed mutation trace."),
        new("command:add-comment", nameof(DomainAuthoringTool.AddComment), new Dictionary<string, object?>(), "Append a comment to a domain object.")
        ,new("command:export-domain-session", nameof(DomainAuthoringTool.ExportDomainSession), new Dictionary<string, object?>(), "Export the current domain session as a portable payload.")
        ,new("command:import-domain-session", nameof(DomainAuthoringTool.ImportDomainSession), new Dictionary<string, object?>(), "Import a domain session payload into a new or preferred session ID.")
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
                    new DomainAffordance("command:create-domain", nameof(DomainAuthoringTool.CreateDomain), new Dictionary<string, object?>(), "Create a new domain session."),
                    new DomainAffordance("command:import-domain-session", nameof(DomainAuthoringTool.ImportDomainSession), new Dictionary<string, object?>(), "Import a domain session payload into a new or preferred session ID.")
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
                .Select(static primitive => new PrimitiveDto(primitive.Name, primitive.Category.ToString(), primitive.IsRequired, primitive.IsNullable))
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
                        .Select(static property => ToPropertyDto(property))
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
                    .Select(static property => ToPropertyDto(property))
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
                    .Select(static property => ToPropertyDto(property))
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

    private static PropertyDto ToPropertyDto(Property property) {
        var localEnum = property.Constraints.OfType<EnumConstraint>().LastOrDefault();
        var effectiveEnum = property.EffectiveConstraints.OfType<EnumConstraint>().LastOrDefault();

        return new PropertyDto(
            Name: property.Name,
            TypeName: property.Type.Name,
            LocalEnumMembers: ToEnumMemberDtos(localEnum),
            EffectiveEnumMembers: ToEnumMemberDtos(effectiveEnum));
    }

    private static IReadOnlyCollection<EnumMemberDto> ToEnumMemberDtos(EnumConstraint? constraint) {
        if (constraint is null) {
            return [];
        }

        return constraint.Members
            .Select(static member => new EnumMemberDto(member.Name, member.CanonicalValue, member.Label))
            .ToArray();
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
public static class DomainOperabilityTool {
    private const string CodeSessionNotFound = "SESSION_NOT_FOUND";
    private const string CodeRevisionNotFound = "REVISION_NOT_FOUND";
    private const string CodeUnsupportedMutation = "UNSUPPORTED_MUTATION";
    private const string CodeAnalysisFailed = "ANALYSIS_FAILED";

    private const string CategoryNotFound = "NotFound";
    private const string CategoryInvalidArgument = "InvalidArgument";
    private const string CategoryAnalysis = "Analysis";

    [McpServerTool, Description("Returns domain health including analyzer telemetry and diagnostic totals.")]
    public static DomainQueryResponse<DomainHealthDto> GetDomainHealth(string sessionId) {
        try {
            var session = RequireSession(sessionId);
            var analyzer = new DomainModelAnalyzer();
            var run = analyzer.AnalyzeWithTelemetry(session.Domain);
            _ = DomainSessionStore.UpdateAnalysis(sessionId, run.Analysis);

            if (!DomainSessionStore.TryGet(sessionId, out var updated)) {
                throw new InvalidOperationException($"Session '{sessionId}' was not found after analysis update.");
            }

            var data = new DomainHealthDto(
                HasErrors: run.Analysis.HasErrors,
                ErrorCount: run.Analysis.Diagnostics.Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                WarningCount: run.Analysis.Diagnostics.Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning),
                TotalAnalysisTime: run.Telemetry.TotalElapsed,
                Incremental: run.Telemetry.Incremental,
                Passes: run.Telemetry.Passes);

            return new DomainQueryResponse<DomainHealthDto>(
                Success: true,
                Message: "Domain health returned.",
                SessionId: sessionId,
                Revision: updated.Revision,
                Data: data,
                Affordances: DomainAffordances.SessionScoped(sessionId));
        }
        catch (Exception ex) {
            var (code, category) = ClassifyAnalysisFailure(ex);
            return Fail<DomainHealthDto>(sessionId, ex, code, category);
        }
    }

    [McpServerTool, Description("Explains invalid domain state by grouping diagnostics per node and attaching remediation hints.")]
    public static DomainQueryResponse<DomainInvalidityDto> ExplainInvalidDomain(string sessionId) {
        try {
            var session = RequireSession(sessionId);
            var analysis = session.LatestAnalysis ?? new DomainModelAnalyzer().Analyze(session.Domain);
            _ = DomainSessionStore.UpdateAnalysis(sessionId, analysis);

            if (!DomainSessionStore.TryGet(sessionId, out var updated)) {
                throw new InvalidOperationException($"Session '{sessionId}' was not found after analysis update.");
            }

            var report = DomainInvalidityExplainer.Explain(analysis);
            var data = new DomainInvalidityDto(report.ErrorCount, report.WarningCount, report.Nodes);

            return new DomainQueryResponse<DomainInvalidityDto>(
                Success: true,
                Message: "Domain invalidity explanation returned.",
                SessionId: sessionId,
                Revision: updated.Revision,
                Data: data,
                Affordances: DomainAffordances.SessionScoped(sessionId));
        }
        catch (Exception ex) {
            var (code, category) = ClassifyAnalysisFailure(ex);
            return Fail<DomainInvalidityDto>(sessionId, ex, code, category);
        }
    }

    [McpServerTool, Description("Diffs two stored domain revisions and returns added, removed, and changed nodes.")]
    public static DomainQueryResponse<DomainRevisionDiffDto> DiffDomainRevision(string sessionId, long fromRevision, long? toRevision = null) {
        try {
            var session = RequireSession(sessionId);
            var targetRevision = toRevision ?? session.Revision;

            if (!DomainSessionStore.TryGetRevisionSnapshot(sessionId, fromRevision, out var fromSnapshot)) {
                throw new InvalidOperationException($"Revision '{fromRevision}' was not found for session '{sessionId}'.");
            }

            if (!DomainSessionStore.TryGetRevisionSnapshot(sessionId, targetRevision, out var toSnapshot)) {
                throw new InvalidOperationException($"Revision '{targetRevision}' was not found for session '{sessionId}'.");
            }

            var analysis = session.LatestAnalysis;
            var diff = DomainDiffUtil.CompareSnapshots(fromSnapshot, toSnapshot, analysis);

            var data = new DomainRevisionDiffDto(
                FromRevision: fromRevision,
                ToRevision: targetRevision,
                AddedCount: diff.Added.Count,
                RemovedCount: diff.Removed.Count,
                ChangedCount: diff.Changed.Count,
                Added: diff.Added,
                Removed: diff.Removed,
                Changed: diff.Changed);

            return new DomainQueryResponse<DomainRevisionDiffDto>(
                Success: true,
                Message: $"Domain diff from revision {fromRevision} to {targetRevision} returned.",
                SessionId: sessionId,
                Revision: session.Revision,
                Data: data,
                Affordances: DomainAffordances.SessionScoped(sessionId));
        }
        catch (Exception ex) {
            var (code, category) = ClassifyDiffFailure(ex);
            return Fail<DomainRevisionDiffDto>(sessionId, ex, code, category);
        }
    }

    [McpServerTool, Description("Applies a mutation and returns detailed mutation trace. Supported mutationType values: SetDomainName, AddPrimitive, AddEntity, AddActor, AddRelationship, AddProperty, AddEventType, AddStage, AddAction, RemoveType.")]
    public static DomainQueryResponse<MutationTraceDto> ApplyMutationWithTrace(
        string sessionId,
        string mutationType,
        string name,
        string? category = null,
        string? parentEntityName = null,
        string? entityName = null,
        string? typeName = null,
        string? sourceName = null,
        string? targetName = null,
        string? cardinality = null,
        bool? sourceOwnsTarget = null) {
        try {
            var state = RequireSession(sessionId);
            var mutation = state.Domain.CreateMutation();

            switch (mutationType) {
                case "SetDomainName":
                    mutation.SetDomainName(name);
                    break;
                case "AddPrimitive": {
                        var typeCategory = ParseTypeCategory(category);
                        mutation.AddType(new Primitive(state.Domain, name, typeCategory));
                        break;
                    }
                case "AddEntity": {
                        var parent = string.IsNullOrWhiteSpace(parentEntityName) ? null : state.Domain.RequireEntity(parentEntityName);
                        mutation.AddType(new Entity(state.Domain, name, parent));
                        break;
                    }
                case "AddActor": {
                        var parent = string.IsNullOrWhiteSpace(parentEntityName) ? null : state.Domain.RequireEntity(parentEntityName);
                        mutation.AddType(new Actor(state.Domain, name, parent));
                        break;
                    }
                case "AddRelationship": {
                        if (string.IsNullOrWhiteSpace(sourceName)) throw new InvalidOperationException("sourceName is required for AddRelationship.");
                        if (string.IsNullOrWhiteSpace(targetName)) throw new InvalidOperationException("targetName is required for AddRelationship.");
                        var src = state.Domain.RequireEntity(sourceName);
                        var tgt = state.Domain.RequireEntity(targetName);
                        var rel = new Relationship(state.Domain, name, src, tgt, ParseCardinality(cardinality), sourceOwnsTarget ?? false);
                        mutation.AddRelationship(rel);
                        break;
                    }
                case "AddProperty": {
                        if (string.IsNullOrWhiteSpace(entityName)) throw new InvalidOperationException("entityName is required for AddProperty.");
                        if (string.IsNullOrWhiteSpace(typeName)) throw new InvalidOperationException("typeName is required for AddProperty.");
                        var propEntity = state.Domain.RequireEntity(entityName);
                        var propType = state.Domain.RequireType(typeName);
                        mutation.AddProperty(propEntity, new Property(state.Domain, name, propType));
                        break;
                    }
                case "AddEventType": {
                        mutation.AddType(new Event(state.Domain, name));
                        break;
                    }
                case "AddStage": {
                        if (string.IsNullOrWhiteSpace(entityName)) throw new InvalidOperationException("entityName is required for AddStage.");
                        var stageEntity = state.Domain.RequireEntity(entityName);
                        mutation.AddStage(stageEntity, new Stage(state.Domain, name));
                        break;
                    }
                case "AddAction": {
                        if (string.IsNullOrWhiteSpace(entityName)) throw new InvalidOperationException("entityName is required for AddAction.");
                        var actionEntity = state.Domain.RequireEntity(entityName);
                        mutation.AddAction(actionEntity, new Poly.Data.Modeling.Action(state.Domain, name, actionEntity));
                        break;
                    }
                case "RemoveType": {
                        var removeType = state.Domain.GetAvailableTypes().FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal))
                            ?? throw new InvalidOperationException($"Type '{name}' was not found in domain.");
                        mutation.RemoveType(removeType);
                        break;
                    }
                default:
                    throw new InvalidOperationException($"Unsupported mutationType '{mutationType}'. Supported values are SetDomainName, AddPrimitive, AddEntity, AddActor, AddRelationship, AddProperty, AddEventType, AddStage, AddAction, RemoveType.");
            }

            var execution = mutation.ApplyWithTrace(state.LatestAnalysis);
            var revision = DomainSessionStore.UpdateAnalysis(sessionId, execution.Analysis);

            var diagnostics = execution.Analysis.Diagnostics
                .Select(static diagnostic => $"{diagnostic.Severity}: {diagnostic.Code} - {diagnostic.Message}")
                .ToArray();

            var data = new MutationTraceDto(
                Succeeded: execution.Trace.Succeeded,
                RolledBack: execution.Trace.RolledBack,
                AppliedStepCount: execution.Trace.AppliedStepCount,
                Duration: execution.Trace.Duration,
                ErrorCount: execution.Trace.ErrorCount,
                WarningCount: execution.Trace.WarningCount,
                AffectedNodeIds: execution.Trace.AffectedNodeIds,
                Steps: execution.Trace.Steps,
                Diagnostics: diagnostics);

            return new DomainQueryResponse<MutationTraceDto>(
                Success: true,
                Message: $"Mutation '{mutationType}' applied with trace.",
                SessionId: sessionId,
                Revision: revision,
                Data: data,
                Affordances: DomainAffordances.SessionScoped(sessionId));
        }
        catch (Exception ex) {
            var (code, diagnosticCategory) = ClassifyMutationFailure(ex);
            return Fail<MutationTraceDto>(sessionId, ex, code, diagnosticCategory);
        }
    }

    private static DomainSessionState RequireSession(string sessionId) {
        if (!DomainSessionStore.TryGet(sessionId, out var session)) {
            throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        }

        return session;
    }

    private static RelationshipCardinality ParseCardinality(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return RelationshipCardinality.OneToMany;
        }

        if (Enum.TryParse<RelationshipCardinality>(value, ignoreCase: true, out var parsed)) {
            return parsed;
        }

        throw new InvalidOperationException($"Unknown cardinality '{value}'.");
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

    private static (string Code, string Category) ClassifyDiffFailure(Exception ex) {
        if (IsSessionNotFound(ex)) {
            return (CodeSessionNotFound, CategoryNotFound);
        }

        if (IsRevisionNotFound(ex)) {
            return (CodeRevisionNotFound, CategoryNotFound);
        }

        return (CodeAnalysisFailed, CategoryAnalysis);
    }

    private static (string Code, string Category) ClassifyAnalysisFailure(Exception ex) {
        if (IsSessionNotFound(ex)) {
            return (CodeSessionNotFound, CategoryNotFound);
        }

        return (CodeAnalysisFailed, CategoryAnalysis);
    }

    private static (string Code, string Category) ClassifyMutationFailure(Exception ex) {
        if (IsSessionNotFound(ex)) {
            return (CodeSessionNotFound, CategoryNotFound);
        }

        if (IsUnsupportedMutation(ex)) {
            return (CodeUnsupportedMutation, CategoryInvalidArgument);
        }

        return (CodeAnalysisFailed, CategoryAnalysis);
    }

    private static DomainQueryResponse<TPayload> Fail<TPayload>(string sessionId, Exception ex, string code, string category) =>
        new(
            Success: false,
            Message: ex.Message,
            SessionId: sessionId,
            Revision: null,
            Data: default,
            Affordances: [],
            Diagnostics: [
                $"code={code};category={category};message={ex.Message}",
                ex.ToString()
            ]);

    private static bool IsSessionNotFound(Exception ex) =>
        ex is InvalidOperationException && ex.Message.Contains("Session '", StringComparison.Ordinal) && ex.Message.Contains("was not found", StringComparison.Ordinal);

    private static bool IsRevisionNotFound(Exception ex) =>
        ex is InvalidOperationException && ex.Message.Contains("Revision '", StringComparison.Ordinal) && ex.Message.Contains("was not found", StringComparison.Ordinal);

    private static bool IsUnsupportedMutation(Exception ex) =>
        ex is InvalidOperationException && ex.Message.Contains("Unsupported mutationType", StringComparison.Ordinal);
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

    [McpServerTool, Description("Exports the current domain session as a portable payload for persistence or transfer.")]
    public static DomainQueryResponse<DomainSessionExportDto> ExportDomainSession(
        [Description("The session ID.")] string sessionId) {
        try {
            var state = RequireSession(sessionId);
            var payload = BuildExportPayload(state.Domain);
            return new DomainQueryResponse<DomainSessionExportDto>(
                Success: true,
                Message: $"Domain session '{sessionId}' exported.",
                SessionId: sessionId,
                Revision: state.Revision,
                Data: payload,
                Affordances: DomainAffordances.SessionScoped(sessionId));
        }
        catch (Exception ex) {
            return new DomainQueryResponse<DomainSessionExportDto>(
                Success: false,
                Message: ex.Message,
                SessionId: sessionId,
                Revision: null,
                Data: default,
                Affordances: [],
                Diagnostics: [ex.ToString()]);
        }
    }

    [McpServerTool, Description("Imports a portable domain payload into a new or preferred session ID.")]
    public static DomainCommandResponse ImportDomainSession(
        [Description("Portable domain payload previously produced by ExportDomainSession.")] DomainSessionExportDto payload,
        [Description("Optional preferred session ID. A unique ID is generated if omitted.")] string? sessionId = null) {
        try {
            ArgumentNullException.ThrowIfNull(payload);
            var (id, state) = DomainSessionStore.Create(payload.DomainName, sessionId);

            var mutation = state.Domain.CreateMutation();
            var typeByName = new Dictionary<string, DomainType>(StringComparer.Ordinal);
            foreach (var type in state.Domain.Types) {
                typeByName[type.Name] = type;
            }

            var entityByName = new Dictionary<string, Entity>(StringComparer.Ordinal);

            foreach (var primitiveDto in payload.Primitives.OrderBy(static p => p.Name, StringComparer.Ordinal)) {
                if (!typeByName.ContainsKey(primitiveDto.Name)) {
                    var primitive = new Primitive(state.Domain, primitiveDto.Name, ParseTypeCategory(primitiveDto.Category));
                    mutation.AddType(primitive);
                    typeByName[primitive.Name] = primitive;
                }

                var primitiveType = (Primitive)typeByName[primitiveDto.Name];
                ApplyTypeConstraints(mutation, primitiveType, primitiveDto.Constraints);
            }

            Entity EnsureEntity(EntityExportDto dto) {
                if (entityByName.TryGetValue(dto.Name, out var existing)) {
                    return existing;
                }

                Entity? parent = null;
                if (!string.IsNullOrWhiteSpace(dto.ParentEntityName)) {
                    var parentDto = payload.Entities.FirstOrDefault(e => string.Equals(e.Name, dto.ParentEntityName, StringComparison.Ordinal));
                    if (parentDto is null) {
                        throw new InvalidOperationException($"Parent entity '{dto.ParentEntityName}' not found in import payload.");
                    }

                    parent = EnsureEntity(parentDto);
                }

                Entity created = dto.IsActor
                    ? new Actor(state.Domain, dto.Name, parent)
                    : new Entity(state.Domain, dto.Name, parent);

                mutation.AddType(created);
                entityByName[created.Name] = created;
                typeByName[created.Name] = created;
                return created;
            }

            foreach (var entityDto in payload.Entities.OrderBy(static e => e.Name, StringComparer.Ordinal)) {
                _ = EnsureEntity(entityDto);
            }

            foreach (var eventDto in payload.EventTypes.OrderBy(static e => e.Name, StringComparer.Ordinal)) {
                if (typeByName.ContainsKey(eventDto.Name)) {
                    continue;
                }

                var @event = new Event(state.Domain, eventDto.Name);
                mutation.AddType(@event);
                typeByName[@event.Name] = @event;
            }

            foreach (var entityDto in payload.Entities) {
                var entity = entityByName[entityDto.Name];

                foreach (var constraint in BuildConstraints(entityDto.Constraints)) {
                    mutation.AddConstraint(entity, constraint);
                }

                foreach (var propertyDto in entityDto.Properties) {
                    var type = ResolveType(typeByName, propertyDto.TypeName, state.Domain.Name);
                    var property = new Property(state.Domain, propertyDto.Name, type);
                    mutation.AddProperty(entity, property);
                    foreach (var constraint in BuildConstraints(propertyDto.Constraints)) {
                        mutation.AddConstraint(property, constraint);
                    }
                }
            }

            foreach (var eventDto in payload.EventTypes) {
                var eventType = (Event)ResolveType(typeByName, eventDto.Name, state.Domain.Name);
                foreach (var propertyDto in eventDto.Properties) {
                    var type = ResolveType(typeByName, propertyDto.TypeName, state.Domain.Name);
                    var property = new Property(state.Domain, propertyDto.Name, type);
                    mutation.AddProperty(eventType, property);
                    foreach (var constraint in BuildConstraints(propertyDto.Constraints)) {
                        mutation.AddConstraint(property, constraint);
                    }
                }
            }

            foreach (var relationshipDto in payload.Relationships) {
                if (!entityByName.TryGetValue(relationshipDto.SourceEntityName, out var source)) {
                    throw new InvalidOperationException($"Relationship source entity '{relationshipDto.SourceEntityName}' was not found in import payload.");
                }

                if (!entityByName.TryGetValue(relationshipDto.TargetEntityName, out var target)) {
                    throw new InvalidOperationException($"Relationship target entity '{relationshipDto.TargetEntityName}' was not found in import payload.");
                }

                var relationship = new Relationship(state.Domain, relationshipDto.Name, source, target, ParseCardinality(relationshipDto.Cardinality), relationshipDto.SourceOwnsTarget);
                mutation.AddRelationship(relationship).AddEntityRelationship(source, relationship);
            }

            foreach (var entityDto in payload.Entities) {
                var entity = entityByName[entityDto.Name];
                foreach (var actionDto in entityDto.Actions ?? []) {
                    var action = new Data.Modeling.Action(state.Domain, actionDto.Name, entity);
                    mutation.AddAction(entity, action);

                    foreach (var paramDto in actionDto.Parameters) {
                        var paramType = ResolveType(typeByName, paramDto.TypeName, state.Domain.Name);
                        var param = new Property(state.Domain, paramDto.Name, paramType);
                        mutation.AddParameter(action, param);
                        foreach (var constraint in BuildConstraints(paramDto.Constraints)) {
                            mutation.AddConstraint(param, constraint);
                        }
                    }

                    var bindingsByEvent = actionDto.PublishEventBindings
                        .GroupBy(static b => b.EventTypeName, StringComparer.Ordinal);
                    foreach (var eventGroup in bindingsByEvent) {
                        var eventType = (Event)ResolveType(typeByName, eventGroup.Key, state.Domain.Name);
                        var effect = new PublishEvent(state.Domain) { Event = eventType };
                        mutation.AddEffect(action, effect);
                        foreach (var bindingDto in eventGroup) {
                            EventPropertyBindingSource source = bindingDto.SourceKind switch {
                                "ActionParameter" => new EventPropertyBindingSource.ActionParameter(bindingDto.SourceName),
                                "EntityProperty" => new EventPropertyBindingSource.EntityProperty(bindingDto.SourceName),
                                _ => throw new InvalidOperationException($"Unknown sourceKind '{bindingDto.SourceKind}'.")
                            };
                            mutation.SetEventPropertyBinding(action, effect, bindingDto.PropertyName, source);
                        }
                    }
                }
            }

            var analysis = mutation.Apply(state.LatestAnalysis);
            return Commit(id, state.Domain, analysis, $"Imported domain '{payload.DomainName}' into session '{id}'.");
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

    [McpServerTool, Description("Adds a new actor type to the domain.")]
    public static DomainCommandResponse AddActor(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the new actor.")] string name,
        [Description("Name of the parent entity to inherit from, if any.")] string? parentEntityName = null) {
        try {
            var state = RequireSession(sessionId);
            var parent = string.IsNullOrWhiteSpace(parentEntityName) ? null : state.Domain.RequireEntity(parentEntityName);
            var analysis = state.Domain.CreateMutation().AddType(new Actor(state.Domain, name, parent)).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Actor '{name}' added.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Sets the property on an actor type that holds the external subject identifier (maps to the JWT 'sub' claim or equivalent). Pass null or empty to clear.")]
    public static DomainCommandResponse SetActorSubjectProperty(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the actor type to configure.")] string actorName,
        [Description("Name of the actor property that holds the external subject ID. Null or empty to clear.")] string? propertyName = null) {
        try {
            var state = RequireSession(sessionId);
            var actor = state.Domain.RequireActor(actorName);
            var property = string.IsNullOrWhiteSpace(propertyName) ? null : actor.RequireProperty(propertyName);
            var analysis = state.Domain.CreateMutation().SetActorSubjectProperty(actor, property).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, property is null
                ? $"Subject property cleared on actor '{actorName}'."
                : $"Subject property set to '{propertyName}' on actor '{actorName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Sets the claim type used to carry role values for an actor type (e.g. 'role' or 'roles'). Pass null or empty to clear and use the runtime default.")]
    public static DomainCommandResponse SetActorRoleClaimType(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the actor type to configure.")] string actorName,
        [Description("Claim type carrying role values. Null or empty to clear.")] string? roleClaimType = null) {
        try {
            var state = RequireSession(sessionId);
            var actor = state.Domain.RequireActor(actorName);
            var value = string.IsNullOrWhiteSpace(roleClaimType) ? null : roleClaimType;
            var analysis = state.Domain.CreateMutation().SetActorRoleClaimType(actor, value).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, value is null
                ? $"Role claim type cleared on actor '{actorName}'."
                : $"Role claim type set to '{value}' on actor '{actorName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Adds a claim-to-property mapping on an actor type, binding a named principal claim to an actor property.")]
    public static DomainCommandResponse AddActorClaimMapping(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the actor type to configure.")] string actorName,
        [Description("The claim type on the principal (e.g. 'email').")] string claimType,
        [Description("The actor property name that receives the claim value.")] string propertyName) {
        try {
            var state = RequireSession(sessionId);
            var actor = state.Domain.RequireActor(actorName);
            var property = actor.RequireProperty(propertyName);
            var mapping = new ActorClaimMapping(claimType, property);
            var analysis = state.Domain.CreateMutation().AddActorClaimMapping(actor, mapping).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Claim mapping '{claimType}' → '{propertyName}' added to actor '{actorName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Removes a claim-to-property mapping from an actor type by claim type.")]
    public static DomainCommandResponse RemoveActorClaimMapping(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the actor type to configure.")] string actorName,
        [Description("The claim type to remove.")] string claimType) {
        try {
            var state = RequireSession(sessionId);
            var actor = state.Domain.RequireActor(actorName);
            var mapping = actor.ClaimMappings.FirstOrDefault(m => string.Equals(m.ClaimType, claimType, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Claim mapping for '{claimType}' not found on actor '{actorName}'.");
            var analysis = state.Domain.CreateMutation().RemoveActorClaimMapping(actor, mapping).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Claim mapping '{claimType}' removed from actor '{actorName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    // ── Policy tools ─────────────────────────────────────────────────────────

    [McpServerTool, Description("Creates a policy on an entity.")]
    public static DomainCommandResponse AddPolicyToEntity(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity.")] string entityName,
        [Description("Name of the new policy.")] string policyName,
        [Description("Aggregation strategy: All (default) or Any.")] string? strategy = null) {
        try {
            var state = RequireSession(sessionId);
            var intent = new AddPolicyToEntityIntent(entityName, policyName, ParseAggregationStrategy(strategy));
            var analysis = ApplyIntent(state, intent);
            return Commit(sessionId, state.Domain, analysis, $"Policy '{policyName}' added to entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Removes a policy from an entity.")]
    public static DomainCommandResponse RemovePolicyFromEntity(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity.")] string entityName,
        [Description("Name of the policy to remove.")] string policyName) {
        try {
            var state = RequireSession(sessionId);
            var intent = new RemovePolicyFromEntityIntent(entityName, policyName);
            var analysis = ApplyIntent(state, intent);
            return Commit(sessionId, state.Domain, analysis, $"Policy '{policyName}' removed from entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Creates a policy on a stage.")]
    public static DomainCommandResponse AddPolicyToStage(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity owning the stage.")] string entityName,
        [Description("Name of the stage.")] string stageName,
        [Description("Name of the new policy.")] string policyName,
        [Description("Aggregation strategy: All (default) or Any.")] string? strategy = null) {
        try {
            var state = RequireSession(sessionId);
            var intent = new AddPolicyToStageIntent(entityName, stageName, policyName, ParseAggregationStrategy(strategy));
            var analysis = ApplyIntent(state, intent);
            return Commit(sessionId, state.Domain, analysis, $"Policy '{policyName}' added to stage '{stageName}' on entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Removes a policy from a stage.")]
    public static DomainCommandResponse RemovePolicyFromStage(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity owning the stage.")] string entityName,
        [Description("Name of the stage.")] string stageName,
        [Description("Name of the policy to remove.")] string policyName) {
        try {
            var state = RequireSession(sessionId);
            var intent = new RemovePolicyFromStageIntent(entityName, stageName, policyName);
            var analysis = ApplyIntent(state, intent);
            return Commit(sessionId, state.Domain, analysis, $"Policy '{policyName}' removed from stage '{stageName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Creates a policy on a property.")]
    public static DomainCommandResponse AddPolicyToProperty(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity owning the property.")] string entityName,
        [Description("Name of the property.")] string propertyName,
        [Description("Name of the new policy.")] string policyName,
        [Description("Aggregation strategy: All (default) or Any.")] string? strategy = null) {
        try {
            var state = RequireSession(sessionId);
            var intent = new AddPolicyToPropertyIntent(entityName, propertyName, policyName, ParseAggregationStrategy(strategy));
            var analysis = ApplyIntent(state, intent);
            return Commit(sessionId, state.Domain, analysis, $"Policy '{policyName}' added to property '{propertyName}' on entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Removes a policy from a property.")]
    public static DomainCommandResponse RemovePolicyFromProperty(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity owning the property.")] string entityName,
        [Description("Name of the property.")] string propertyName,
        [Description("Name of the policy to remove.")] string policyName) {
        try {
            var state = RequireSession(sessionId);
            var intent = new RemovePolicyFromPropertyIntent(entityName, propertyName, policyName);
            var analysis = ApplyIntent(state, intent);
            return Commit(sessionId, state.Domain, analysis, $"Policy '{policyName}' removed from property '{propertyName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Creates a policy on an action.")]
    public static DomainCommandResponse AddPolicyToAction(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity owning the action.")] string entityName,
        [Description("Name of the action.")] string actionName,
        [Description("Name of the new policy.")] string policyName,
        [Description("Aggregation strategy: All (default) or Any.")] string? strategy = null) {
        try {
            var state = RequireSession(sessionId);
            var intent = new AddPolicyToActionIntent(entityName, actionName, policyName, ParseAggregationStrategy(strategy));
            var analysis = ApplyIntent(state, intent);
            return Commit(sessionId, state.Domain, analysis, $"Policy '{policyName}' added to action '{actionName}' on entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Removes a policy from an action.")]
    public static DomainCommandResponse RemovePolicyFromAction(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity owning the action.")] string entityName,
        [Description("Name of the action.")] string actionName,
        [Description("Name of the policy to remove.")] string policyName) {
        try {
            var state = RequireSession(sessionId);
            var intent = new RemovePolicyFromActionIntent(entityName, actionName, policyName);
            var analysis = ApplyIntent(state, intent);
            return Commit(sessionId, state.Domain, analysis, $"Policy '{policyName}' removed from action '{actionName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    // ── Rule tools ───────────────────────────────────────────────────────────

    [McpServerTool, Description("Adds a cross-property comparison rule to a policy.")]
    public static DomainCommandResponse AddCrossPropertyRuleToPolicy(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity owning the policy.")] string entityName,
        [Description("Name of the policy.")] string policyName,
        [Description("Name for the new rule.")] string ruleName,
        [Description("Name of the left-hand property.")] string leftPropertyName,
        [Description("Name of the right-hand property.")] string rightPropertyName,
        [Description("Comparison operator: Equal, NotEqual, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual.")] string @operator,
        [Description("Stage owning the policy, if any.")] string? stageName = null,
        [Description("Action owning the policy, if any.")] string? actionName = null,
        [Description("Property owning the policy, if any.")] string? propertyName = null) {
        try {
            var state = RequireSession(sessionId);
            var op = Enum.Parse<DomainComparisonOperator>(@operator, ignoreCase: true);
            var intent = new AddCrossPropertyRuleToPolicyIntent(entityName, policyName, ruleName, leftPropertyName, rightPropertyName, op, stageName, actionName, propertyName);
            var analysis = ApplyIntent(state, intent);
            return Commit(sessionId, state.Domain, analysis, $"CrossProperty rule '{ruleName}' added to policy '{policyName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Adds a rule to a policy requiring the evaluating actor to be of the specified actor type.")]
    public static DomainCommandResponse AddActorTypeRuleToPolicy(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity owning the policy.")] string entityName,
        [Description("Name of the policy.")] string policyName,
        [Description("Name for the new rule.")] string ruleName,
        [Description("Name of the actor type the principal must be.")] string actorTypeName,
        [Description("Stage owning the policy, if any.")] string? stageName = null,
        [Description("Action owning the policy, if any.")] string? actionName = null,
        [Description("Property owning the policy, if any.")] string? propertyName = null) {
        try {
            var state = RequireSession(sessionId);
            var intent = new AddActorTypeRuleToPolicyIntent(entityName, policyName, ruleName, actorTypeName, stageName, actionName, propertyName);
            var analysis = ApplyIntent(state, intent);
            return Commit(sessionId, state.Domain, analysis, $"ActorType rule '{ruleName}' added to policy '{policyName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Adds a rule to a policy requiring the evaluating actor to have a specific role.")]
    public static DomainCommandResponse AddActorRoleRuleToPolicy(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity owning the policy.")] string entityName,
        [Description("Name of the policy.")] string policyName,
        [Description("Name for the new rule.")] string ruleName,
        [Description("Role value the actor must have.")] string role,
        [Description("Stage owning the policy, if any.")] string? stageName = null,
        [Description("Action owning the policy, if any.")] string? actionName = null,
        [Description("Property owning the policy, if any.")] string? propertyName = null) {
        try {
            var state = RequireSession(sessionId);
            var intent = new AddActorRoleRuleToPolicyIntent(entityName, policyName, ruleName, role, stageName, actionName, propertyName);
            var analysis = ApplyIntent(state, intent);
            return Commit(sessionId, state.Domain, analysis, $"ActorRole rule '{ruleName}' (role='{role}') added to policy '{policyName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Adds a rule that checks an equality constraint against a property on the evaluating actor.")]
    public static DomainCommandResponse AddActorPropertyRuleToPolicy(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity owning the policy.")] string entityName,
        [Description("Name of the policy.")] string policyName,
        [Description("Name for the new rule.")] string ruleName,
        [Description("Name of the actor type owning the property.")] string actorTypeName,
        [Description("Name of the actor property to constrain.")] string actorPropertyName,
        [Description("Value the actor property must equal.")] object constraintValue,
        [Description("Stage owning the policy, if any.")] string? stageName = null,
        [Description("Action owning the policy, if any.")] string? actionName = null,
        [Description("Property owning the policy, if any.")] string? propertyName = null) {
        try {
            var state = RequireSession(sessionId);
            var intent = new AddActorPropertyRuleToPolicyIntent(entityName, policyName, ruleName, actorTypeName, actorPropertyName, constraintValue, stageName, actionName, propertyName);
            var analysis = ApplyIntent(state, intent);
            return Commit(sessionId, state.Domain, analysis, $"ActorProperty rule '{ruleName}' ({actorTypeName}.{actorPropertyName} == {constraintValue}) added to policy '{policyName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Combines two existing rules in the same policy with And or Or.")]
    public static DomainCommandResponse AddCompositeRuleToPolicy(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity owning the policy.")] string entityName,
        [Description("Name of the policy.")] string policyName,
        [Description("Name for the new composite rule.")] string ruleName,
        [Description("Name of the left rule.")] string leftRuleName,
        [Description("Name of the right rule.")] string rightRuleName,
        [Description("Logical operator: And or Or.")] string @operator,
        [Description("Stage owning the policy, if any.")] string? stageName = null,
        [Description("Action owning the policy, if any.")] string? actionName = null,
        [Description("Property owning the policy, if any.")] string? propertyName = null) {
        try {
            var state = RequireSession(sessionId);
            var op = Enum.Parse<LogicalOperator>(@operator, ignoreCase: true);
            var intent = new AddCompositeRuleToPolicyIntent(entityName, policyName, ruleName, leftRuleName, rightRuleName, op, stageName, actionName, propertyName);
            var analysis = ApplyIntent(state, intent);
            return Commit(sessionId, state.Domain, analysis, $"Composite rule '{ruleName}' ({leftRuleName} {op} {rightRuleName}) added to policy '{policyName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Removes a rule from a policy.")]
    public static DomainCommandResponse RemoveRuleFromPolicy(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity owning the policy.")] string entityName,
        [Description("Name of the policy.")] string policyName,
        [Description("Name of the rule to remove.")] string ruleName,
        [Description("Stage owning the policy, if any.")] string? stageName = null,
        [Description("Action owning the policy, if any.")] string? actionName = null,
        [Description("Property owning the policy, if any.")] string? propertyName = null) {
        try {
            var state = RequireSession(sessionId);
            var intent = new RemoveRuleFromPolicyIntent(entityName, policyName, ruleName, stageName, actionName, propertyName);
            var analysis = ApplyIntent(state, intent);
            return Commit(sessionId, state.Domain, analysis, $"Rule '{ruleName}' removed from policy '{policyName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    private static PolicyAggregationStrategy ParseAggregationStrategy(string? value) {
        if (string.IsNullOrWhiteSpace(value)) return PolicyAggregationStrategy.All;
        if (Enum.TryParse<PolicyAggregationStrategy>(value, ignoreCase: true, out var parsed)) return parsed;
        throw new InvalidOperationException($"Unknown aggregation strategy '{value}'. Expected 'All' or 'Any'.");
    }

    private static AnalysisResult ApplyIntent(DomainSessionState state, DomainMutationIntent intent) =>
        new DomainMutationIntentEngine().Apply(state.Domain, intent, preMutationAnalysis: state.LatestAnalysis);

    [McpServerTool, Description("Adds a primitive type to the domain.")]
    public static DomainCommandResponse AddPrimitive(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the new primitive type.")] string name,
        [Description("Type category (e.g. Numeric, Text, Temporal). Defaults to Primitive.")] string? category = null) {
        try {
            var state = RequireSession(sessionId);
            var typeCategory = ParseTypeCategory(category);
            ValidatePrimitiveCategoryForDomainModeling(typeCategory, name);
            var analysis = state.Domain.CreateMutation().AddType(new Primitive(state.Domain, name, typeCategory)).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Primitive '{name}' ({typeCategory}) added.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Adds or replaces a closed enum constraint on an existing domain type.")]
    public static DomainCommandResponse AddEnumConstraintToType(
        [Description("The session ID.")] string sessionId,
        [Description("Name of an existing domain type.")] string typeName,
        [Description("Closed enum members. Each member has Name and optional CanonicalValue/Label.")] EnumMemberDto[] members) {
        try {
            var state = RequireSession(sessionId);
            var type = state.Domain.RequireType(typeName);
            var enumConstraint = BuildEnumConstraint(members);

            var mutation = state.Domain.CreateMutation();
            foreach (var existing in type.Constraints.Where(static constraint => constraint.IsOrContains<EnumConstraint>()).ToArray()) {
                mutation.RemoveConstraint(type, existing);
            }

            var analysis = mutation
                .AddConstraint(type, enumConstraint)
                .Apply(state.LatestAnalysis);

            return Commit(sessionId, state.Domain, analysis, $"Enum constraint applied to type '{typeName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Adds or replaces a closed enum constraint on an entity property. Property-level enum overrides type-level enum.")]
    public static DomainCommandResponse AddEnumConstraintToEntityProperty(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the target entity.")] string entityName,
        [Description("Name of the target property.")] string propertyName,
        [Description("Closed enum members. Each member has Name and optional CanonicalValue/Label.")] EnumMemberDto[] members) {
        try {
            var state = RequireSession(sessionId);
            var entity = state.Domain.RequireEntity(entityName);
            var property = entity.RequireProperty(propertyName);
            var enumConstraint = BuildEnumConstraint(members);

            var mutation = state.Domain.CreateMutation();
            foreach (var existing in property.Constraints.Where(static constraint => constraint.IsOrContains<EnumConstraint>()).ToArray()) {
                mutation.RemoveConstraint(property, existing);
            }

            var analysis = mutation
                .AddConstraint(property, enumConstraint)
                .Apply(state.LatestAnalysis);

            return Commit(sessionId, state.Domain, analysis, $"Enum constraint applied to property '{propertyName}' on entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Returns all available constraint types.")]
    public static DomainQueryResponse<IReadOnlyCollection<ConstraintTypeDto>> GetAvailableConstraintTypes(
        [Description("The session ID.")] string sessionId) {
        try {
            var state = RequireSession(sessionId);

            var available = new List<ConstraintTypeDto> {
                new ConstraintTypeDto("Length", "Length", new[] { "minLength", "maxLength" }),
                new ConstraintTypeDto("Range", "Range", new[] { "minValue", "maxValue" }),
                new ConstraintTypeDto("Required", "Required", Array.Empty<string>()),
                new ConstraintTypeDto("Enum", "Enum", new[] { "members" }),
                new ConstraintTypeDto("Equality", "Equality", new[] { "value" })
            };

            return new DomainQueryResponse<IReadOnlyCollection<ConstraintTypeDto>>(
                Success: true,
                Message: $"Found {available.Count} constraint types.",
                SessionId: sessionId,
                Revision: state.Revision,
                Data: available.ToArray(),
                Affordances: DomainAffordances.SessionScoped(sessionId));
        }
        catch (Exception ex) {
            return new DomainQueryResponse<IReadOnlyCollection<ConstraintTypeDto>>(
                Success: false,
                Message: ex.Message,
                SessionId: sessionId,
                Revision: null,
                Data: default,
                Affordances: [],
                Diagnostics: [ex.ToString()]);
        }
    }

    [McpServerTool, Description("Adds or replaces a constraint on an existing domain type by constraint type name.")]
    public static DomainCommandResponse AddConstraintToType(
        [Description("The session ID.")] string sessionId,
        [Description("Name of an existing domain type.")] string typeName,
        [Description("Constraint type name: Length, Range, Required, Enum, Equality.")] string constraintType,
        [Description("For Length: minimum length. Null means no minimum.")] int? minLength = null,
        [Description("For Length: maximum length. Null means no maximum.")] int? maxLength = null,
        [Description("For Range: minimum value. Null means no minimum.")] object? minValue = null,
        [Description("For Range: maximum value. Null means no maximum.")] object? maxValue = null,
        [Description("For Enum: closed enum members (each has Name, optional CanonicalValue/Label).")] EnumMemberDto[]? members = null,
        [Description("For Equality: the value to equal.")] object? value = null) {
        try {
            var state = RequireSession(sessionId);
            var type = state.Domain.RequireType(typeName);

            var (constraint, constraintName) = BuildConstraint(constraintType, minLength, maxLength, minValue, maxValue, members, value);

            var mutation = state.Domain.CreateMutation();
            foreach (var existing in type.Constraints.Where(c => c.GetType().Name.StartsWith(constraint.GetType().Name.Replace("Constraint", ""), StringComparison.Ordinal)).ToArray()) {
                mutation.RemoveConstraint(type, existing);
            }

            var analysis = mutation
                .AddConstraint(type, constraint)
                .Apply(state.LatestAnalysis);

            return Commit(sessionId, state.Domain, analysis, $"{constraintName} constraint applied to type '{typeName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Adds or replaces a constraint on an entity property by constraint type name. Property-level overrides type-level.")]
    public static DomainCommandResponse AddConstraintToEntityProperty(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the target entity.")] string entityName,
        [Description("Name of the target property.")] string propertyName,
        [Description("Constraint type name: Length, Range, Required, Enum, Equality.")] string constraintType,
        [Description("For Length: minimum length. Null means no minimum.")] int? minLength = null,
        [Description("For Length: maximum length. Null means no maximum.")] int? maxLength = null,
        [Description("For Range: minimum value. Null means no minimum.")] object? minValue = null,
        [Description("For Range: maximum value. Null means no maximum.")] object? maxValue = null,
        [Description("For Enum: closed enum members (each has Name, optional CanonicalValue/Label).")] EnumMemberDto[]? members = null,
        [Description("For Equality: the value to equal.")] object? value = null) {
        try {
            var state = RequireSession(sessionId);
            var entity = state.Domain.RequireEntity(entityName);
            var property = entity.RequireProperty(propertyName);

            var (constraint, constraintName) = BuildConstraint(constraintType, minLength, maxLength, minValue, maxValue, members, value);

            var mutation = state.Domain.CreateMutation();
            foreach (var existing in property.Constraints.Where(c => c.GetType().Name.StartsWith(constraint.GetType().Name.Replace("Constraint", ""), StringComparison.Ordinal)).ToArray()) {
                mutation.RemoveConstraint(property, existing);
            }

            var analysis = mutation
                .AddConstraint(property, constraint)
                .Apply(state.LatestAnalysis);

            return Commit(sessionId, state.Domain, analysis, $"{constraintName} constraint applied to property '{propertyName}' on entity '{entityName}'.");
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
        [Description("Name of an existing domain type for this property (e.g. \"Text\", \"DateTime\"). Use GetDomain to see available types.")] string typeName) {
        try {
            var state = RequireSession(sessionId);
            var entity = state.Domain.RequireEntity(entityName);
            var mutation = state.Domain.CreateMutation();
            var type = state.Domain.RequireType(typeName);
            var analysis = mutation.AddProperty(entity, new Property(state.Domain, propertyName, type)).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Property '{propertyName}' added to entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Adds a typed property to an event type.")]
    public static DomainCommandResponse AddPropertyToEventType(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the target event type.")] string eventTypeName,
        [Description("Name of the new property.")] string propertyName,
        [Description("Name of an existing domain type for this property (e.g. \"Text\", \"DateTime\"). Use GetDomain to see available types.")] string typeName) {
        try {
            var state = RequireSession(sessionId);
            var eventType = state.Domain.RequireEventType(eventTypeName);
            var mutation = state.Domain.CreateMutation();
            var type = state.Domain.RequireType(typeName);
            var analysis = mutation.AddProperty(eventType, new Property(state.Domain, propertyName, type)).Apply(state.LatestAnalysis);
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

    [McpServerTool, Description("Removes a type (entity, primitive, or event type) from the domain by name.")]
    public static DomainCommandResponse RemoveType(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the type to remove.")] string typeName) {
        try {
            var state = RequireSession(sessionId);
            var type = state.Domain.Types.FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Type '{typeName}' was not found in domain '{state.Domain.Name}'.");
            var analysis = state.Domain.CreateMutation().RemoveType(type).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Type '{typeName}' removed.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Removes a relationship from the domain, including the source entity back-reference.")]
    public static DomainCommandResponse RemoveRelationship(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the relationship to remove.")] string name) {
        try {
            var state = RequireSession(sessionId);
            var relationship = state.Domain.RequireRelationship(name);
            var analysis = state.Domain.CreateMutation()
                .RemoveEntityRelationship(relationship.Source, relationship)
                .RemoveRelationship(relationship)
                .Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Relationship '{name}' removed.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Updates the source, target, cardinality, and ownership of an existing relationship.")]
    public static DomainCommandResponse SetRelationshipShape(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the relationship to update.")] string relationshipName,
        [Description("Name of the new source entity.")] string sourceEntityName,
        [Description("Name of the new target entity.")] string targetEntityName,
        [Description("Cardinality: OneToOne, OneToMany, ManyToOne, or ManyToMany.")] string cardinality = nameof(RelationshipCardinality.OneToMany),
        [Description("Whether the source entity owns and controls the lifecycle of target instances.")] bool sourceOwnsTarget = false) {
        try {
            var state = RequireSession(sessionId);
            var relationship = state.Domain.RequireRelationship(relationshipName);
            var source = state.Domain.RequireEntity(sourceEntityName);
            var target = state.Domain.RequireEntity(targetEntityName);
            var analysis = state.Domain.CreateMutation()
                .SetRelationship(relationship, source, target, ParseCardinality(cardinality), sourceOwnsTarget)
                .Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Relationship '{relationshipName}' shape updated.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Removes a property from an entity.")]
    public static DomainCommandResponse RemovePropertyFromEntity(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity.")] string entityName,
        [Description("Name of the property to remove.")] string propertyName) {
        try {
            var state = RequireSession(sessionId);
            var entity = state.Domain.RequireEntity(entityName);
            var property = entity.RequireProperty(propertyName);
            var analysis = state.Domain.CreateMutation().RemoveProperty(entity, property).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Property '{propertyName}' removed from entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Removes a property from an event type.")]
    public static DomainCommandResponse RemovePropertyFromEventType(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the event type.")] string eventTypeName,
        [Description("Name of the property to remove.")] string propertyName) {
        try {
            var state = RequireSession(sessionId);
            var eventType = state.Domain.RequireEventType(eventTypeName);
            var property = eventType.RequireProperty(propertyName);
            var analysis = state.Domain.CreateMutation().RemoveProperty(eventType, property).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Property '{propertyName}' removed from event type '{eventTypeName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Removes a lifecycle stage from an entity.")]
    public static DomainCommandResponse RemoveStageFromEntity(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity.")] string entityName,
        [Description("Name of the stage to remove.")] string stageName) {
        try {
            var state = RequireSession(sessionId);
            var entity = state.Domain.RequireEntity(entityName);
            var stage = entity.RequireStage(stageName);
            var analysis = state.Domain.CreateMutation().RemoveStage(entity, stage).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Stage '{stageName}' removed from entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Removes an action from an entity.")]
    public static DomainCommandResponse RemoveActionFromEntity(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity.")] string entityName,
        [Description("Name of the action to remove.")] string actionName) {
        try {
            var state = RequireSession(sessionId);
            var entity = state.Domain.RequireEntity(entityName);
            var action = entity.RequireAction(actionName);
            var analysis = state.Domain.CreateMutation().RemoveAction(entity, action).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Action '{actionName}' removed from entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Removes an event type association from an entity.")]
    public static DomainCommandResponse RemoveEventFromEntity(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity.")] string entityName,
        [Description("Name of the event type to disassociate.")] string eventTypeName) {
        try {
            var state = RequireSession(sessionId);
            var entity = state.Domain.RequireEntity(entityName);
            var @event = entity.RequireEvent(eventTypeName);
            var analysis = state.Domain.CreateMutation().RemoveEvent(entity, @event).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Event '{eventTypeName}' removed from entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Assigns an existing entity action to a lifecycle stage, making it available in that stage.")]
    public static DomainCommandResponse AddActionToStage(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity.")] string entityName,
        [Description("Name of the stage.")] string stageName,
        [Description("Name of the action to assign to the stage.")] string actionName) {
        try {
            var state = RequireSession(sessionId);
            var entity = state.Domain.RequireEntity(entityName);
            var stage = entity.RequireStage(stageName);
            var action = entity.RequireAction(actionName);
            var analysis = state.Domain.CreateMutation().AddAction(stage, action).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Action '{actionName}' assigned to stage '{stageName}' on entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Removes an action assignment from a lifecycle stage.")]
    public static DomainCommandResponse RemoveActionFromStage(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity.")] string entityName,
        [Description("Name of the stage.")] string stageName,
        [Description("Name of the action to remove from the stage.")] string actionName) {
        try {
            var state = RequireSession(sessionId);
            var entity = state.Domain.RequireEntity(entityName);
            var stage = entity.RequireStage(stageName);
            var action = stage.RequireAction(actionName);
            var analysis = state.Domain.CreateMutation().RemoveAction(stage, action).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Action '{actionName}' removed from stage '{stageName}' on entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Adds a typed parameter to an action.")]
    public static DomainCommandResponse AddParameterToAction(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity owning the action.")] string entityName,
        [Description("Name of the action.")] string actionName,
        [Description("Name of the new parameter.")] string parameterName,
        [Description("Name of an existing domain type for this parameter (e.g. \"Uuid\", \"Text\"). Use GetDomain to see available types.")] string typeName) {
        try {
            var state = RequireSession(sessionId);
            var entity = state.Domain.RequireEntity(entityName);
            var action = entity.RequireAction(actionName);
            var mutation = state.Domain.CreateMutation();
            var type = state.Domain.RequireType(typeName);
            var analysis = mutation.AddParameter(action, new Property(state.Domain, parameterName, type)).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Parameter '{parameterName}' added to action '{actionName}' on entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Removes a parameter from an action.")]
    public static DomainCommandResponse RemoveParameterFromAction(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity owning the action.")] string entityName,
        [Description("Name of the action.")] string actionName,
        [Description("Name of the parameter to remove.")] string parameterName) {
        try {
            var state = RequireSession(sessionId);
            var entity = state.Domain.RequireEntity(entityName);
            var action = entity.RequireAction(actionName);
            var parameter = action.RequireParameter(parameterName);
            var analysis = state.Domain.CreateMutation().RemoveParameter(action, parameter).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Parameter '{parameterName}' removed from action '{actionName}' on entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Adds a PublishEvent effect to an action. After adding, use SetEventPropertyBinding to bind each event property.")]
    public static DomainCommandResponse AddPublishEventEffect(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity owning the action.")] string entityName,
        [Description("Name of the action.")] string actionName,
        [Description("Name of the event type to publish.")] string eventTypeName) {
        try {
            var state = RequireSession(sessionId);
            var entity = state.Domain.RequireEntity(entityName);
            var action = entity.RequireAction(actionName);
            var eventType = state.Domain.RequireEventType(eventTypeName);
            var effect = new PublishEvent(state.Domain) { Event = eventType };
            var analysis = state.Domain.CreateMutation().AddEffect(action, effect).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"PublishEvent '{eventTypeName}' added to action '{actionName}' on entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Binds an event property to a value source on a PublishEvent effect. sourceKind must be 'ActionParameter' or 'EntityProperty'. sourceName is the parameter or property name.")]
    public static DomainCommandResponse SetEventPropertyBinding(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity owning the action.")] string entityName,
        [Description("Name of the action.")] string actionName,
        [Description("Name of the event type being published.")] string eventTypeName,
        [Description("Name of the event property to bind.")] string propertyName,
        [Description("Source kind: 'ActionParameter' or 'EntityProperty'.")] string sourceKind,
        [Description("Name of the action parameter or entity property that provides the value.")] string sourceName) {
        try {
            var state = RequireSession(sessionId);
            var entity = state.Domain.RequireEntity(entityName);
            var action = entity.RequireAction(actionName);
            var eventType = state.Domain.RequireEventType(eventTypeName);

            var effect = action.Effects.OfType<PublishEvent>().FirstOrDefault(e => ReferenceEquals(e.Event, eventType))
                ?? throw new InvalidOperationException($"Action '{actionName}' does not have a PublishEvent effect for '{eventTypeName}'. Add it first with AddPublishEventEffect.");

            EventPropertyBindingSource source = sourceKind switch {
                "ActionParameter" => new EventPropertyBindingSource.ActionParameter(sourceName),
                "EntityProperty" => new EventPropertyBindingSource.EntityProperty(sourceName),
                _ => throw new InvalidOperationException($"Unknown sourceKind '{sourceKind}'. Must be 'ActionParameter' or 'EntityProperty'.")
            };

            var analysis = state.Domain.CreateMutation().SetEventPropertyBinding(action, effect, propertyName, source).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"Binding set: event property '{propertyName}' ← {sourceKind} '{sourceName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Removes a PublishEvent effect from an action.")]
    public static DomainCommandResponse RemovePublishEventEffect(
        [Description("The session ID.")] string sessionId,
        [Description("Name of the entity owning the action.")] string entityName,
        [Description("Name of the action.")] string actionName,
        [Description("Name of the event type whose PublishEvent effect should be removed.")] string eventTypeName) {
        try {
            var state = RequireSession(sessionId);
            var entity = state.Domain.RequireEntity(entityName);
            var action = entity.RequireAction(actionName);
            var eventType = state.Domain.RequireEventType(eventTypeName);

            var effect = action.Effects.OfType<PublishEvent>().FirstOrDefault(e => ReferenceEquals(e.Event, eventType))
                ?? throw new InvalidOperationException($"Action '{actionName}' does not have a PublishEvent effect for '{eventTypeName}'.");

            var analysis = state.Domain.CreateMutation().RemoveEffect(action, effect).Apply(state.LatestAnalysis);
            return Commit(sessionId, state.Domain, analysis, $"PublishEvent '{eventTypeName}' removed from action '{actionName}' on entity '{entityName}'.");
        }
        catch (Exception ex) { return Fail(sessionId, ex); }
    }

    [McpServerTool, Description("Appends a comment to a domain object by path.")]
    public static DomainCommandResponse AddComment(string sessionId, string nodePath, string comment) {
        if (!DomainSessionStore.TryGet(sessionId, out var session))
            return new(false, $"Session '{sessionId}' not found.", sessionId, null, null, [], ["Session not found."]);
        var engine = new DomainMutationIntentEngine();
        var intent = new AddCommentIntent(nodePath, comment);
        var analysis = engine.Apply(session.Domain, intent);
        DomainSessionStore.UpdateAnalysis(sessionId, analysis);
        return new(true, $"Comment added to '{nodePath}'.", sessionId, session.Domain.Name, session.Revision + 1, DomainAffordances.SessionScoped(sessionId), null);
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

        var hasErrors = analysis.Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        return new DomainCommandResponse(
            Success: !hasErrors,
            Message: hasErrors ? $"{message} Validation failed and mutation was rolled back." : message,
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

    private static void ValidatePrimitiveCategoryForDomainModeling(TypeCategory category, string primitiveName) {
        if (category.Is(TypeCategory.Nullable)) {
            throw new InvalidOperationException(
                $"Primitive '{primitiveName}' cannot use TypeCategory.Nullable. Domain nullability is modeled by RequiredConstraint.");
        }

        if (category.Is(TypeCategory.Collection) || category.Is(TypeCategory.Keyed)) {
            throw new InvalidOperationException(
                $"Primitive '{primitiveName}' cannot use collection categories (Collection/Keyed). Domain multiplicity is modeled through relationships.");
        }
    }

    private static EnumConstraint BuildEnumConstraint(IEnumerable<EnumMemberDto> members) {
        ArgumentNullException.ThrowIfNull(members);

        var materialized = members
            .Select(static member => new EnumConstraint.EnumMember(member.Name, member.CanonicalValue, member.Label))
            .ToArray();

        if (materialized.Length == 0) {
            throw new InvalidOperationException("Enum constraint requires at least one member.");
        }

        return new EnumConstraint(materialized);
    }

    private static (Constraint Constraint, string Name) BuildConstraint(
        string constraintType,
        int? minLength,
        int? maxLength,
        object? minValue,
        object? maxValue,
        EnumMemberDto[]? members,
        object? value) {
        return constraintType.ToLowerInvariant() switch {
            "length" => (new LengthConstraint(minLength, maxLength), "Length"),
            "range" => (new RangeConstraint(minValue, maxValue), "Range"),
            "required" => (new RequiredConstraint(), "Required"),
            "equality" => value is null
                ? throw new ArgumentException("Equality constraint requires a non-null value.", nameof(value))
                : (new EqualityConstraint(value), "Equality"),
            "enum" => (BuildEnumConstraint(members ?? []), "Enum"),
            _ => throw new ArgumentException($"Unknown constraint type '{constraintType}'. Use GetAvailableConstraintTypes to see valid types.")
        };
    }

    private static DomainSessionExportDto BuildExportPayload(Domain domain) {
        var primitives = domain.GetAvailablePrimitives()
            .OrderBy(static primitive => primitive.Name, StringComparer.Ordinal)
            .Select(static primitive => new PrimitiveExportDto(
                primitive.Name,
                primitive.Category.ToString(),
                ToConstraintDtos(primitive.Constraints)))
            .ToArray();

        var entities = domain.GetAvailableEntities()
            .Where(static entity => entity is not Relationship)
            .OrderBy(static entity => entity.Name, StringComparer.Ordinal)
            .Select(static entity => new EntityExportDto(
                Name: entity.Name,
                IsActor: entity is Actor,
                ParentEntityName: entity.ParentEntity?.Name,
                Constraints: ToConstraintDtos(entity.Constraints),
                Properties: entity.Properties
                    .OrderBy(static property => property.Name, StringComparer.Ordinal)
                    .Select(static property => new PropertyExportDto(property.Name, property.Type.Name, ToConstraintDtos(property.Constraints)))
                    .ToArray(),
                Actions: entity.Actions
                    .OrderBy(static action => action.Name, StringComparer.Ordinal)
                    .Select(static action => new ActionExportDto(
                        Name: action.Name,
                        Parameters: action.Parameters
                            .Select(static p => new PropertyExportDto(p.Name, p.Type.Name, ToConstraintDtos(p.Constraints)))
                            .ToArray(),
                        PublishEventBindings: action.Effects.OfType<PublishEvent>()
                            .SelectMany(static pe => pe.PropertyBindings
                                .Select(kvp => new EventPropertyBindingExportDto(
                                    EventTypeName: pe.Event.Name,
                                    PropertyName: kvp.Key,
                                    SourceKind: kvp.Value switch {
                                        EventPropertyBindingSource.ActionParameter => "ActionParameter",
                                        EventPropertyBindingSource.EntityProperty => "EntityProperty",
                                        _ => "Unknown"
                                    },
                                    SourceName: kvp.Value switch {
                                        EventPropertyBindingSource.ActionParameter ap => ap.ParameterName,
                                        EventPropertyBindingSource.EntityProperty ep => ep.PropertyName,
                                        _ => string.Empty
                                    })))
                            .ToArray()))
                    .ToArray()))
            .ToArray();

        var eventTypes = domain.GetAvailableEventTypes()
            .OrderBy(static @event => @event.Name, StringComparer.Ordinal)
            .Select(static @event => new EventTypeExportDto(
                Name: @event.Name,
                Properties: @event.Properties
                    .OrderBy(static property => property.Name, StringComparer.Ordinal)
                    .Select(static property => new PropertyExportDto(property.Name, property.Type.Name, ToConstraintDtos(property.Constraints)))
                    .ToArray()))
            .ToArray();

        var relationships = domain.GetAvailableRelationships()
            .OrderBy(static relationship => relationship.Name, StringComparer.Ordinal)
            .Select(static relationship => new RelationshipExportDto(
                relationship.Name,
                relationship.Source.Name,
                relationship.Target.Name,
                relationship.Cardinality.ToString(),
                relationship.SourceOwnsTarget))
            .ToArray();

        return new DomainSessionExportDto(domain.Name, primitives, entities, eventTypes, relationships);
    }

    private static IReadOnlyCollection<ConstraintDto> ToConstraintDtos(IEnumerable<Constraint> constraints) {
        return constraints.Select(ToConstraintDto).Where(static dto => dto is not null).Select(static dto => dto!).ToArray();
    }

    private static ConstraintDto? ToConstraintDto(Constraint constraint) {
        return constraint switch {
            RequiredConstraint => new ConstraintDto("Required"),
            EqualityConstraint equality => new ConstraintDto("Equality", Value: equality.Value),
            RangeConstraint range => new ConstraintDto("Range", MinValue: range.MinValue, MaxValue: range.MaxValue),
            LengthConstraint length => new ConstraintDto("Length", MinLength: length.MinLength, MaxLength: length.MaxLength),
            EnumConstraint @enum => new ConstraintDto("Enum", EnumMembers: @enum.Members.Select(static m => new EnumMemberDto(m.Name, m.CanonicalValue, m.Label)).ToArray()),
            _ => null
        };
    }

    private static IEnumerable<Constraint> BuildConstraints(IEnumerable<ConstraintDto> constraints) {
        foreach (var dto in constraints ?? []) {
            switch (dto.Kind) {
                case "Required":
                    yield return new RequiredConstraint();
                    break;
                case "Equality":
                    yield return new EqualityConstraint(dto.Value!);
                    break;
                case "Range":
                    yield return new RangeConstraint(dto.MinValue, dto.MaxValue);
                    break;
                case "Length":
                    yield return new LengthConstraint(dto.MinLength, dto.MaxLength);
                    break;
                case "Enum":
                    yield return BuildEnumConstraint(dto.EnumMembers ?? []);
                    break;
            }
        }
    }

    private static void ApplyTypeConstraints(Domain.Mutation mutation, DomainType type, IReadOnlyCollection<ConstraintDto> constraintDtos) {
        foreach (var existing in type.Constraints.ToArray()) {
            mutation.RemoveConstraint(type, existing);
        }

        foreach (var constraint in BuildConstraints(constraintDtos)) {
            mutation.AddConstraint(type, constraint);
        }
    }

    private static DomainType ResolveType(IDictionary<string, DomainType> typeByName, string typeName, string domainName) {
        if (typeByName.TryGetValue(typeName, out var type)) {
            return type;
        }

        throw new InvalidOperationException($"Type '{typeName}' was not found in import payload for domain '{domainName}'.");
    }
}