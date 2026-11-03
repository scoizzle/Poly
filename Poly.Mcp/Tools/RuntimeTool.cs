using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Server;

using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Mcp.Sessions;

namespace Poly.Mcp.Tools;

/// <summary>
/// Runtime MCP tools: instance creation, inspection, and action execution.
/// They run the lowered operation module through the session's
/// <see cref="ModuleInstanceExecutor"/> — not new domain IR. An instance is the
/// module's dictionary, tracked by executor id; the tool layer only converts JSON
/// and renders snapshots.
/// </summary>
[McpServerToolType]
internal sealed class RuntimeTool(ISessionStore store, ILogger<RuntimeTool> logger) {
    // ── Shared helpers ──────────────────────────────────────────

    /// <summary>Rendered stage for an entity that defines none.</summary>
    private const string NoStage = "(none)";

    /// <summary>Rendered value for a collection nav (and for an unreachable peer).</summary>
    private const string LinkedMarker = "#linked";

    private static string RenderStage(string? stage) => stage ?? NoStage;

    private static DomainToolResponse Failure_NotFound(string sessionId) =>
        new(Success: false,
            Message: $"Session '{sessionId}' not found.",
            Affordances: ["create_domain_session", "list_sessions"]);

    private static object? JsonElementToValue(JsonElement je) => je.ValueKind switch {
        JsonValueKind.Number => JsonNumber(je),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => je.GetString(),
        JsonValueKind.Null => null,
        _ => je.GetRawText()
    };

    /// <summary>
    /// A JSON number stays a <c>long</c> when it is integral, including <c>1.0</c>.
    /// A fraction stays a <c>decimal</c> so the property type can accept or reject it.
    /// Values outside both ranges still fail.
    /// </summary>
    private static object JsonNumber(JsonElement je) {
        if (je.TryGetInt64(out var integral))
            return integral;
        if (je.TryGetDecimal(out var dec))
            return dec;
        throw new InvalidOperationException(
            $"Number is integral; '{je.GetRawText()}' is not a valid Number. Use Decimal.");
    }

    /// <summary>
    /// One property value for an MCP snapshot. Scalars render as their string form.
    /// A navigation renders as <c>#instanceId</c> — the handle the instance tools
    /// take — or <c>#linked</c> when the peer is not reachable by id in this session;
    /// a collection nav is <c>#linked</c> (its ids stay in navigationLinks). No
    /// runtime type ever reaches the answer.
    /// </summary>
    private static string RenderPropertyValue(object? value, ModuleInstanceExecutor executor) =>
        value switch {
            IDictionary<string, object?> navigation =>
                executor.TryFind(navigation, out var peer) && peer is not null ? $"#{peer.Id}" : LinkedMarker,
            string text => text,
            System.Collections.IEnumerable => LinkedMarker,
            _ => value?.ToString() ?? "(null)"
        };

    private const string CurrentStageKey = "CurrentStage";

    /// <summary>
    /// The stage the instance is in. The module leaves the key unset until the first
    /// transition, so an unset key on a staged entity is its first stage.
    /// </summary>
    private static string? StageOf(McpSessionState state, ModuleInstance instance) =>
        instance.Bag.TryGetValue(CurrentStageKey, out var stage) && stage is not null
            ? Convert.ToString(stage, CultureInfo.InvariantCulture)
            : EntityOf(state, instance)?.Stages.FirstOrDefault()?.Name;

    private static Entity? EntityOf(McpSessionState state, ModuleInstance instance) =>
        state.Domain.Types.OfType<Entity>()
            .FirstOrDefault(e => string.Equals(e.Name, instance.EntityName, StringComparison.Ordinal));

    /// <summary>
    /// The properties an MCP snapshot reports: the entity's declared properties, then
    /// each to-one navigation the bag holds. The stage key, collection fields and the
    /// subscriber registry stay in the bag for the module and are not properties.
    /// </summary>
    private static List<KeyValuePair<string, object?>> SnapshotEntries(
        McpSessionState state, ModuleInstance instance) {
        var entries = new List<KeyValuePair<string, object?>>();
        var declared = new HashSet<string>(StringComparer.Ordinal);
        if (EntityOf(state, instance) is { } entity) {
            foreach (var property in entity.Properties) {
                declared.Add(property.Name);
                if (instance.Bag.TryGetValue(property.Name, out var value))
                    entries.Add(new(property.Name, value));
            }
        }
        foreach (var (key, value) in instance.Bag) {
            if (!declared.Contains(key) && value is IDictionary<string, object?>)
                entries.Add(new(key, value));
        }
        return entries;
    }

    private static InstanceSnapshotData BuildSnapshot(
        McpSessionState state, ModuleInstance instance, int createdChildCount = 0) {
        var executor = state.Executor;
        var props = new List<PropertyValueData>();
        foreach (var (key, value) in SnapshotEntries(state, instance))
            props.Add(new PropertyValueData(key, RenderPropertyValue(value, executor)));
        return new InstanceSnapshotData(
            InstanceId: instance.Id,
            EntityName: instance.EntityName,
            CurrentStage: RenderStage(StageOf(state, instance)),
            Properties: props,
            CreatedChildCount: createdChildCount,
            NavigationLinks: []
        );
    }

    // ── DTOs ────────────────────────────────────────────────────

    internal sealed record PropertyValueData(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("value")] string Value
    );

    internal sealed record InstanceSnapshotData(
        [property: JsonPropertyName("instanceId")] string InstanceId,
        [property: JsonPropertyName("entityName")] string EntityName,
        [property: JsonPropertyName("currentStage")] string CurrentStage,
        [property: JsonPropertyName("properties")] IReadOnlyList<PropertyValueData> Properties,
        [property: JsonPropertyName("createdChildCount")] int CreatedChildCount,
        [property: JsonPropertyName("navigationLinks")] IReadOnlyList<NavigationLinkData> NavigationLinks
    );

    internal sealed record NavigationLinkData(
        [property: JsonPropertyName("relationshipName")] string RelationshipName,
        [property: JsonPropertyName("direction")] string Direction,
        [property: JsonPropertyName("linkedInstanceIds")] IReadOnlyList<string> LinkedInstanceIds
    );

    internal sealed record InstanceSummaryData(
        [property: JsonPropertyName("instanceId")] string InstanceId,
        [property: JsonPropertyName("entityName")] string EntityName,
        [property: JsonPropertyName("currentStage")] string CurrentStage,
        [property: JsonPropertyName("propertyCount")] int PropertyCount
    );

    internal sealed record InvokeActionResultData(
        [property: JsonPropertyName("actionName")] string ActionName,
        [property: JsonPropertyName("succeeded")] bool Succeeded,
        [property: JsonPropertyName("newStage")] string? NewStage,
        [property: JsonPropertyName("failedGuards")] IReadOnlyList<string>? FailedGuards,
        [property: JsonPropertyName("errorMessage")] string? ErrorMessage,
        [property: JsonPropertyName("returnTypeName")] string? ReturnTypeName = null,
        [property: JsonPropertyName("returnInstanceId")] string? ReturnInstanceId = null
    );

    // ── RT.1: create_instance ──────────────────────────────────

    /// <summary>
    /// Creates a runtime instance of a domain entity. The instance is registered
    /// in the session's instance store, enabling lifecycle operations, action
    /// execution, and stage subscription fan-out. Returns the instance ID and
    /// a snapshot of initial stage and property values.
    /// </summary>
    [McpServerTool(Name = "create_instance"), Description(@"Creates an instance of a domain entity in this session.

Starts in the first defined stage when the entity has stages. Returns an instance id and a snapshot of initial property values.

Use invoke_action to run actions, evaluate_policy to check policies, link_instances to connect related instances, and get_instance / list_instances to inspect.")]
    public DomainToolResponse CreateInstance(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity to instantiate")] string entityName,
        [Description("Optional JSON object of initial property values, " +
            "e.g. {\"Name\":\"Alice\",\"Age\":30,\"Status\":\"Active\"}. Values are coerced to the " +
            "declared property types: Date/DateTime/Time/Duration properties accept ISO strings " +
            "(\"2026-01-01\", \"2026-01-01T00:00:00Z\", \"09:00:00\", \"00:30:00\"), Guid accepts " +
            "guid strings, and wrong-typed values fail the call.")]
        string? propertiesJson = null) {
        if (!store.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);
        logger.LogDebug("create_instance {SessionId} {Entity}", sessionId, entityName);

        // Resolve entity
        var entity = state.Domain.Types.OfType<Entity>()
            .FirstOrDefault(e => string.Equals(e.Name, entityName, StringComparison.Ordinal));
        if (entity is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Entity '{entityName}' not found in domain '{state.Domain.Name}'. " +
                    $"Available: {string.Join(", ", state.Domain.Types.OfType<Entity>().Select(e => e.Name))}.",
                SessionId: sessionId,
                Affordances: ["get_domain_overview", "add"]);

        // Parse property values
        Dictionary<string, object?>? propertyValues = null;
        if (!string.IsNullOrWhiteSpace(propertiesJson)) {
            try {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(propertiesJson);
                if (parsed is not null) {
                    propertyValues = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var (key, je) in parsed)
                        propertyValues[key] = JsonElementToValue(je);
                }
            }
            catch (Exception ex) {
                return new DomainToolResponse(
                    Success: false,
                    Message: $"Invalid properties JSON: {ex.Message}",
                    SessionId: sessionId,
                    Affordances: ["get_entity_detail"]);
            }
        }

        var unknown = propertyValues?.Keys.FirstOrDefault(key =>
            !entity.Properties.Any(p => string.Equals(p.Name, key, StringComparison.Ordinal)));
        if (unknown is not null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Failed to create instance: Property '{unknown}' does not exist on entity '{entity.Name}'. " +
                    $"Available: {string.Join(", ", entity.Properties.Select(p => p.Name))}.",
                SessionId: sessionId,
                Affordances: ["get_entity_detail"]);

        // The executor runs the cached static Create on a dictionary, coerces the
        // JSON-ish values to the declared types, and registers the instance by id.
        ModuleOutcome outcome;
        try {
            outcome = state.Executor.Create(
                entityName, propertyValues ?? new Dictionary<string, object?>(StringComparer.Ordinal));
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Failed to create instance: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_entity_detail"]);
        }
        if (!outcome.Succeeded || outcome.Instance is not { } instance)
            return new DomainToolResponse(
                Success: false,
                Message: $"Failed to create instance: {outcome.Error}",
                SessionId: sessionId,
                Affordances: ["get_entity_detail"]);

        var snapshot = BuildSnapshot(state, instance, outcome.CreatedChildren.Count);
        return new DomainToolResponse(
            Success: true,
            Message: $"Instance '{instance.Id}' of entity '{entityName}' created. Stage: '{RenderStage(StageOf(state, instance))}'.",
            SessionId: sessionId,
            Data: new { instance = snapshot },
            Affordances: ["get_instance", "invoke_action", "evaluate_policy", "link_instances", "list_instances"]);
    }

    // ── RT.2: link_instances ───────────────────────────────────

    /// <summary>
    /// Links two runtime instances via a named relationship, creating an edge
    /// in the session's instance store. Enables cross-entity policy evaluation
    /// (e.g. Collection quantifiers like <c>any orders where Total > 100</c>) and
    /// stage-subscription fan-out.
    ///
    /// Both instances must already exist (created via <c>create_instance</c>).
    /// The relationship must be defined in the domain model between the entities.
    /// Calling Link multiple times with the same arguments is idempotent.
    ///
    /// To evaluate a policy that reads linked targets, call
    /// <c>evaluate_policy(instanceId=sourceId)</c> after linking.
    /// </summary>
    [McpServerTool(Name = "link_instances"), Description(@"Links two runtime instances via a named relationship.

Both instances must already exist (created via create_instance).
The relationship must be defined in the domain model between the source and target entities.

After linking, you can evaluate cross-entity policies by passing the
source instance's ID to evaluate_policy(instanceId=...).

Idempotent: calling link_instances with the same arguments multiple times
is safe and will not create duplicate edges.

Use case — Collection quantifiers:
  1. create_instance for source entity (e.g. Customer)
  2. create_instance for target entity (e.g. Order)
  3. link_instances with relationship name (e.g. ""orders"")
  4. evaluate_policy(entityName, policyName, instanceId=sourceId)")]
    public DomainToolResponse LinkInstances(
        [Description("Session ID")] string sessionId,
        [Description("Instance ID of the source (owning) instance")] string sourceInstanceId,
        [Description("Relationship name as defined in the domain (e.g. 'orders', 'loans')")] string relationshipName,
        [Description("Instance ID of the target (owned) instance")] string targetInstanceId) {
        if (!store.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        if (state.ExecutorIfCreated is not { } executor || !executor.TryGet(sourceInstanceId, out var source) || source is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Source instance '{sourceInstanceId}' not found.",
                SessionId: sessionId,
                Affordances: ["create_instance", "list_instances"]);

        if (!executor.TryGet(targetInstanceId, out var target) || target is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Target instance '{targetInstanceId}' not found.",
                SessionId: sessionId,
                Affordances: ["create_instance", "list_instances"]);

        // Validate relationship exists in domain model and entity ends match.
        // Relationship identity is (source entity, name); the instances disambiguate
        // a name declared on multiple source entities.
        var relationship = (state.LatestAnalysis?.GetAllRelationships(state.Domain) ?? [])
            .FirstOrDefault(r => string.Equals(r.Name, relationshipName, StringComparison.Ordinal)
                && (string.Equals(r.Source.TypeName, source.EntityName, StringComparison.Ordinal)
                    || string.Equals(r.Target.TypeName, source.EntityName, StringComparison.Ordinal)));
        if (relationship is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Relationship '{relationshipName}' not found in domain '{state.Domain.Name}'. " +
                    $"Available: {string.Join(", ", (state.LatestAnalysis?.GetAllRelationships(state.Domain) ?? []).Select(r => r.Name))}.",
                SessionId: sessionId,
                Affordances: ["get_relationships"]);

        var sourceEntityName = source.EntityName;
        var targetEntityName = target.EntityName;
        var sourceMatch = string.Equals(relationship.Source.TypeName, sourceEntityName, StringComparison.Ordinal);
        var targetMatch = string.Equals(relationship.Target.TypeName, targetEntityName, StringComparison.Ordinal);
        if (!sourceMatch || !targetMatch) {
            // Also check reversed (target is source side of relationship)
            var revSourceMatch = string.Equals(relationship.Source.TypeName, targetEntityName, StringComparison.Ordinal);
            var revTargetMatch = string.Equals(relationship.Target.TypeName, sourceEntityName, StringComparison.Ordinal);
            if (revSourceMatch && revTargetMatch)
                return new DomainToolResponse(
                    Success: false,
                    Message: $"Relationship '{relationshipName}' connects '{relationship.Source.TypeName}' → '{relationship.Target.TypeName}', " +
                        $"but the source/target instance IDs are reversed for a directed link. " +
                        $"Pass source instance (entity '{relationship.Source.TypeName}') first, then target (entity '{relationship.Target.TypeName}').",
                    SessionId: sessionId,
                    Affordances: ["get_relationships", "get_entity_detail"]);

            return new DomainToolResponse(
                Success: false,
                Message: $"Relationship '{relationshipName}' connects '{relationship.Source.TypeName}' → '{relationship.Target.TypeName}', " +
                    $"but the provided instances are of types '{sourceEntityName}' and '{targetEntityName}'.",
                SessionId: sessionId,
                Affordances: ["get_relationships", "get_entity_detail"]);
        }

        ModuleOutcome linked;
        try {
            linked = executor.Link(relationshipName, source, target);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Failed to link instances: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_relationships", "get_entity_detail"]);
        }
        if (!linked.Succeeded)
            return new DomainToolResponse(
                Success: false,
                Message: $"Failed to link instances: {linked.Error}",
                SessionId: sessionId,
                Affordances: ["get_relationships", "get_entity_detail"]);

        return new DomainToolResponse(
            Success: true,
            Message: $"Linked '{sourceInstanceId}' → '{targetInstanceId}' via '{relationshipName}'.",
            SessionId: sessionId,
            Data: new { sourceInstanceId, relationshipName, targetInstanceId },
            Affordances: ["link_instances", "evaluate_policy", "invoke_action", "list_instances"]);
    }

    // ── RT.2b: unlink_instances ────────────────────────────────

    /// <summary>
    /// Removes a link between two runtime instances for a named relationship.
    /// Both instances must already exist and be linked via <c>link_instances</c>.
    /// Fails if the link does not exist (fail-closed).
    ///
    /// Use case — reassign a child entity from one parent to another:
    ///   1. unlink_instances to remove child from old parent
    ///   2. link_instances to attach child to new parent
    /// </summary>
    [McpServerTool(Name = "unlink_instances"), Description(@"Removes a link between two runtime instances for a named relationship.

Both instances must already exist and be linked via link_instances.
Fails if the link does not exist — use link_instances to create the link first.

Use case — reassign a child from one parent to another:
  1. unlink_instances(sessionId, oldParentId, ""relationshipName"", childId)
  2. link_instances(sessionId, newParentId, ""relationshipName"", childId)")]
    public DomainToolResponse UnlinkInstances(
        [Description("Session ID")] string sessionId,
        [Description("Instance ID of the source (owning) instance")] string sourceInstanceId,
        [Description("Relationship name as defined in the domain (e.g. 'orders', 'loans')")] string relationshipName,
        [Description("Instance ID of the target (owned) instance")] string targetInstanceId) {
        if (!store.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        if (state.ExecutorIfCreated is not { } executor || !executor.TryGet(sourceInstanceId, out var source) || source is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Source instance '{sourceInstanceId}' not found.",
                SessionId: sessionId,
                Affordances: ["create_instance", "list_instances"]);

        if (!executor.TryGet(targetInstanceId, out var target) || target is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Target instance '{targetInstanceId}' not found.",
                SessionId: sessionId,
                Affordances: ["create_instance", "list_instances"]);

        // Validate relationship exists in domain model and entity ends match.
        // Relationship identity is (source entity, name); the instances disambiguate
        // a name declared on multiple source entities.
        var relationship = (state.LatestAnalysis?.GetAllRelationships(state.Domain) ?? [])
            .FirstOrDefault(r => string.Equals(r.Name, relationshipName, StringComparison.Ordinal)
                && (string.Equals(r.Source.TypeName, source.EntityName, StringComparison.Ordinal)
                    || string.Equals(r.Target.TypeName, source.EntityName, StringComparison.Ordinal)));
        if (relationship is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Relationship '{relationshipName}' not found in domain '{state.Domain.Name}'. " +
                    $"Available: {string.Join(", ", (state.LatestAnalysis?.GetAllRelationships(state.Domain) ?? []).Select(r => r.Name))}.",
                SessionId: sessionId,
                Affordances: ["get_relationships"]);

        var sourceEntityName = source.EntityName;
        var targetEntityName = target.EntityName;
        var sourceMatch = string.Equals(relationship.Source.TypeName, sourceEntityName, StringComparison.Ordinal);
        var targetMatch = string.Equals(relationship.Target.TypeName, targetEntityName, StringComparison.Ordinal);
        if (!sourceMatch || !targetMatch) {
            var revSourceMatch = string.Equals(relationship.Source.TypeName, targetEntityName, StringComparison.Ordinal);
            var revTargetMatch = string.Equals(relationship.Target.TypeName, sourceEntityName, StringComparison.Ordinal);
            if (revSourceMatch && revTargetMatch)
                return new DomainToolResponse(
                    Success: false,
                    Message: $"Relationship '{relationshipName}' connects '{relationship.Source.TypeName}' → '{relationship.Target.TypeName}', " +
                        $"but the source/target instance IDs are reversed. " +
                        $"Pass source instance (entity '{relationship.Source.TypeName}') first, then target (entity '{relationship.Target.TypeName}').",
                    SessionId: sessionId,
                    Affordances: ["get_relationships", "get_entity_detail"]);

            return new DomainToolResponse(
                Success: false,
                Message: $"Relationship '{relationshipName}' connects '{relationship.Source.TypeName}' → '{relationship.Target.TypeName}', " +
                    $"but the provided instances are of types '{sourceEntityName}' and '{targetEntityName}'.",
                SessionId: sessionId,
                Affordances: ["get_relationships", "get_entity_detail"]);
        }

        // Fail-closed: link must exist to unlink
        if (!executor.IsLinked(relationshipName, source, target))
            return new DomainToolResponse(
                Success: false,
                Message: $"No link found between '{sourceInstanceId}' and '{targetInstanceId}' via '{relationshipName}'. " +
                    $"Use link_instances to create the link first.",
                SessionId: sessionId,
                Affordances: ["link_instances", "get_relationships"]);

        ModuleOutcome unlinked;
        try {
            unlinked = executor.Unlink(relationshipName, source, target);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Failed to unlink instances: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_relationships", "get_entity_detail"]);
        }
        if (!unlinked.Succeeded)
            return new DomainToolResponse(
                Success: false,
                Message: $"Failed to unlink instances: {unlinked.Error}",
                SessionId: sessionId,
                Affordances: ["get_relationships", "get_entity_detail"]);

        return new DomainToolResponse(
            Success: true,
            Message: $"Unlinked '{sourceInstanceId}' -/→ '{targetInstanceId}' via '{relationshipName}'.",
            SessionId: sessionId,
            Data: new { sourceInstanceId, relationshipName, targetInstanceId },
            Affordances: ["link_instances", "evaluate_policy", "invoke_action", "list_instances", "unlink_instances"]);
    }

    // ── RT.3: get_instance ─────────────────────────────────────

    /// <summary>
    /// Returns a full snapshot of a runtime instance: current stage, all property values,
    /// deletion status, and count of child instances created by effects.
    /// </summary>
    [McpServerTool(Name = "get_instance"), Description("Returns a snapshot of a runtime instance: current stage, property values, deletion status, and created-child count.")]
    public DomainToolResponse GetInstance(
        [Description("Session ID")] string sessionId,
        [Description("Instance ID returned by create_instance")] string instanceId) {
        if (!store.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        if (state.ExecutorIfCreated is not { } executor
            || !executor.TryGet(instanceId, out var instance) || instance is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Instance '{instanceId}' not found.",
                SessionId: sessionId,
                Affordances: ["create_instance", "list_instances"]);

        var snapshot = BuildSnapshot(state, instance);

        // Navigation links come from the bag's relationship fields, per relationship.
        var navs = new List<NavigationLinkData>();
        var entityName = instance.EntityName;
        foreach (var rel in state.LatestAnalysis?.GetAllRelationships(state.Domain) ?? []) {
            var isSource = string.Equals(rel.Source.TypeName, entityName, StringComparison.Ordinal);
            var isTarget = string.Equals(rel.Target.TypeName, entityName, StringComparison.Ordinal);
            if (!isSource && !isTarget) continue;

            // Same relationship name can exist on two sources (Enrollment.section
            // and WaitOffer.section). Scope peers to this relationship's types.
            var ids = (isSource
                    ? executor.LinkedTargets(rel.Name, instance)
                        .Where(peer => string.Equals(peer.EntityName, rel.Target.TypeName, StringComparison.Ordinal))
                    : executor.LinkedSources(rel.Name, instance)
                        .Where(peer => string.Equals(peer.EntityName, rel.Source.TypeName, StringComparison.Ordinal)))
                .Select(peer => peer.Id)
                .ToList();
            if (ids.Count == 0) continue;
            navs.Add(new NavigationLinkData(rel.Name, isSource ? "source→target" : "target→source", ids));
        }

        snapshot = snapshot with { NavigationLinks = navs };

        return new DomainToolResponse(
            Success: true,
            Message: $"Instance '{instanceId}' of '{instance.EntityName}', stage: '{RenderStage(StageOf(state, instance))}'.",
            SessionId: sessionId,
            Data: new { instance = snapshot },
            Affordances: ["invoke_action", "list_instances", "create_instance"]);
    }

    // ── RT.4: list_instances ───────────────────────────────────

    /// <summary>
    /// Lists all runtime instances in the session, optionally filtered by entity name.
    /// Returns summary data: instance ID, entity name, current stage, and property count.
    /// </summary>
    [McpServerTool(Name = "list_instances"), Description("Lists all runtime instances in the session, optionally filtered by entity name.")]
    public DomainToolResponse ListInstances(
        [Description("Session ID")] string sessionId,
        [Description("Optional entity name filter — only list instances of this entity type")] string? entityName = null) {
        if (!store.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        var summaries = new List<InstanceSummaryData>();
        foreach (var instance in state.ExecutorIfCreated?.Instances ?? []) {
            if (entityName is not null &&
                !string.Equals(instance.EntityName, entityName, StringComparison.Ordinal))
                continue;
            summaries.Add(new InstanceSummaryData(instance.Id, instance.EntityName,
                RenderStage(StageOf(state, instance)), SnapshotEntries(state, instance).Count));
        }

        return new DomainToolResponse(
            Success: true,
            Message: $"Found {summaries.Count} instance(s).",
            SessionId: sessionId,
            Data: new { instances = summaries, count = summaries.Count },
            Affordances: summaries.Count > 0
                ? ["get_instance", "invoke_action", "create_instance"]
                : ["create_instance"]);
    }

    // ── RT.5: invoke_action ─────────────────────────────────────

    /// <summary>
    /// Calls an action on a runtime instance. The action is resolved from the
    /// current stage (stage-scoped actions) or entity-level actions.
    ///
    /// The action pipeline: resolves action → evaluates guard policies →
    /// executes effects (transition, assign, create, create-in, invoke, for-invoke, if).
    /// Linking existing instances is <c>link_instances</c> / <c>unlink_instances</c>,
    /// not an action effect.
    ///
    /// On success, returns the new stage (if a transition occurred). On failure,
    /// returns which guard policies blocked the action or why the action was not found.
    ///
    /// Stage subscription fan-out happens automatically when a transition occurs:
    /// linked subscriber instances see the transition and their subscription
    /// effects are executed.
    /// </summary>
    [McpServerTool(Name = "invoke_action"), Description(@"Runs a named action on an instance from create_instance.

Resolves the action from the instance's current stage, then entity-level actions. Guard policies run first. Actions may transition, assign, create, create-in, invoke, for-invoke, if. On success, returns the new stage when a transition occurred. On failure, returns which guards blocked or why the action was not found. Linked subscribers fire on a stage transition.

Entity-typed parameters take instance ids from create_instance. Actions that return an entity include returnTypeName and returnInstanceId.

To link or unlink existing instances, use link_instances / unlink_instances — those are not action effects.")]
    public DomainToolResponse InvokeAction(
        [Description("Session ID")] string sessionId,
        [Description("Instance ID returned by create_instance")] string instanceId,
        [Description("Name of the action to invoke")] string actionName,
        [Description("Optional JSON object of action parameter values, " +
            "e.g. {\"amount\":100,\"reason\":\"urgent\"}")]
        string? argsJson = null) {
        if (!store.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        if (state.ExecutorIfCreated is not { } executor
            || !executor.TryGet(instanceId, out var instance) || instance is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Instance '{instanceId}' not found.",
                SessionId: sessionId,
                Affordances: ["create_instance", "list_instances"]);

        // Parse action args JSON
        Dictionary<string, object?>? args = null;
        if (!string.IsNullOrWhiteSpace(argsJson)) {
            try {
                var parsed = System.Text.Json.JsonSerializer
                    .Deserialize<Dictionary<string, JsonElement>>(argsJson);
                if (parsed is not null) {
                    args = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var (key, je) in parsed)
                        args[key] = JsonElementToValue(je);
                }
            }
            catch (Exception ex) {
                return new DomainToolResponse(
                    Success: false,
                    Message: $"Invalid args JSON: {ex.Message}",
                    SessionId: sessionId,
                    Affordances: ["get_instance"]);
            }
        }

        // Entity-typed arguments are instance ids; the executor resolves them to the
        // registered dictionaries, and the module body owns guards, stage and effects.
        ModuleOutcome outcome;
        try {
            outcome = executor.Invoke(instance, actionName, args);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Action execution failed: {ex.Message}",
                SessionId: sessionId,
                Data: new { actionName, error = ex.Message },
                Affordances: ["get_instance", "list_instances"]);
        }

        var action = FindAction(state, instance, actionName);
        var returnedType = action?.Result.Members.Count > 0 ? action.Result.Members[0].Type.TypeName : null;
        var errorMessage = outcome.Error;
        IReadOnlyList<string>? failedGuards = null;
        var succeeded = outcome.Succeeded;
        if (succeeded && returnedType is not null && outcome.Instance is null) {
            succeeded = false;
            errorMessage = $"Action '{actionName}' declared return type '{returnedType}' but no create/create-in " +
                "produced an instance of that type during this invoke.";
        }
        if (!succeeded && action is not null && MapGuardFailure(actionName, action, errorMessage) is { } mapped) {
            failedGuards = mapped.Guards;
            errorMessage = mapped.Message;
        }

        var returnInstanceId = succeeded ? outcome.Instance?.Id : null;
        var returnTypeName = succeeded ? outcome.Instance?.EntityName ?? returnedType : returnedType;
        var newStage = succeeded ? StageOf(state, instance) : null;
        var resultData = new InvokeActionResultData(
            ActionName: actionName,
            Succeeded: succeeded,
            NewStage: newStage,
            FailedGuards: failedGuards is { Count: > 0 } ? failedGuards : null,
            ErrorMessage: errorMessage,
            ReturnTypeName: returnTypeName,
            ReturnInstanceId: returnInstanceId
        );

        var successMessage = succeeded
            ? returnInstanceId is not null
                ? $"Action '{actionName}' succeeded. Returned '{returnTypeName}' as instance '{returnInstanceId}'. New stage: '{newStage ?? "(unchanged)"}'."
                : $"Action '{actionName}' succeeded. New stage: '{newStage ?? "(unchanged)"}'."
            : (errorMessage ?? $"Action '{actionName}' blocked by guards: {string.Join(", ", failedGuards ?? [])}");

        return new DomainToolResponse(
            Success: succeeded,
            Message: successMessage,
            SessionId: sessionId,
            Data: new { invokeActionResult = resultData },
            Affordances: ["get_instance", "list_instances", "create_instance"]);
    }

    private static Poly.DomainModeling.Ontology.Action? FindAction(
        McpSessionState state, ModuleInstance instance, string actionName) =>
        EntityOf(state, instance) is { } entity
            ? entity.Actions.Concat(entity.Stages.SelectMany(s => s.Actions))
                .FirstOrDefault(a => string.Equals(a.Name, actionName, StringComparison.Ordinal))
            : null;

    /// <summary>
    /// The module reports a blocked guard as a Failure message. A policy block names the
    /// guard (mapped to the action's own policy name); a failed path-prefix require
    /// keeps its message and lists every guard on the action.
    /// </summary>
    private static (IReadOnlyList<string> Guards, string? Message)? MapGuardFailure(
        string actionName, Poly.DomainModeling.Ontology.Action action, string? message) {
        if (message is null)
            return null;
        const string blockedPrefix = "blocked by policy '";
        var blockedIdx = message.IndexOf(blockedPrefix, StringComparison.Ordinal);
        if (blockedIdx >= 0 && message.StartsWith($"'{actionName}'", StringComparison.Ordinal)) {
            var start = blockedIdx + blockedPrefix.Length;
            var end = message.IndexOf('\'', start);
            if (end > start) {
                var leaf = message[start..end];
                var guard = action.Policies.Select(p => p.Name).FirstOrDefault(n =>
                    string.Equals(n, leaf, StringComparison.Ordinal)
                    || string.Equals(n, "not_" + leaf, StringComparison.Ordinal)) ?? leaf;
                return ([guard], null);
            }
        }
        if (message.Contains("requires a linked", StringComparison.Ordinal))
            return ([.. action.Policies.Select(p => p.Name)], message);
        return null;
    }
}