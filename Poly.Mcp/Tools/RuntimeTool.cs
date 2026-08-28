using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

using ModelContextProtocol.Server;

using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Mcp.Sessions;

namespace Poly.Mcp.Tools;

/// <summary>
/// Runtime MCP tools: instance creation, inspection, and action execution.
/// These wrap existing <see cref="DomainEntityInstance"/> and
/// <see cref="DomainInstanceStore"/> machinery — not new domain IR.
/// Session-scoped instances live on <see cref="McpSessionState.InstanceMap"/>
/// and are registered in <see cref="McpSessionState.InstanceStore"/> for
/// relationship/subscription support.
/// </summary>
[McpServerToolType]
internal sealed class RuntimeTool {
    // ── Shared helpers ──────────────────────────────────────────

    private static DomainToolResponse Failure_NotFound(string sessionId) =>
        new(Success: false,
            Message: $"Session '{sessionId}' not found.",
            Affordances: ["create_domain_session", "list_sessions"]);

    private static string NewInstanceId() => Guid.NewGuid().ToString("N");

    private static Dictionary<string, object?>? BindEntityTypedActionArgs(
        McpSessionState state,
        DomainEntityInstance instance,
        string actionName,
        Dictionary<string, object?>? args) {
        if (args is null || args.Count == 0)
            return args;

        Poly.DomainModeling.Ontology.Action? action = null;
        if (instance.CurrentStage is { } stageName) {
            var stage = instance.Entity.Stages.FirstOrDefault(s =>
                string.Equals(s.Name, stageName, StringComparison.Ordinal));
            action = stage?.Actions.FirstOrDefault(a =>
                string.Equals(a.Name, actionName, StringComparison.Ordinal));
        }
        action ??= instance.Entity.Actions.FirstOrDefault(a =>
            string.Equals(a.Name, actionName, StringComparison.Ordinal));
        if (action is null)
            return args;

        var entityNames = state.Domain.Types.OfType<Entity>()
            .Select(e => e.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var param in action.Parameters) {
            if (!entityNames.Contains(param.Type.TypeName))
                continue;
            if (!args.TryGetValue(param.Name, out var raw) || raw is not string id)
                continue;
            if (state.InstanceMap.TryGetValue(id, out var linked))
                args[param.Name] = linked;
        }

        return args;
    }

    private static object? JsonElementToValue(JsonElement je) => je.ValueKind switch {
        JsonValueKind.Number when je.TryGetInt32(out var i) => (long)i,
        JsonValueKind.Number when je.TryGetInt64(out var l) => l,
        JsonValueKind.Number => (long)je.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => je.GetString(),
        JsonValueKind.Null => null,
        _ => je.GetRawText()
    };

    private static InstanceSnapshotData BuildSnapshot(DomainEntityInstance instance) {
        var props = new List<PropertyValueData>();
        foreach (var (key, value) in instance.Snapshot())
            props.Add(new PropertyValueData(key, value?.ToString() ?? "(null)"));
        return new InstanceSnapshotData(
            InstanceId: "",
            EntityName: instance.Entity.Name,
            CurrentStage: instance.CurrentStage,
            Properties: props,
            CreatedChildCount: instance.CreatedChildren.Count,
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
        [property: JsonPropertyName("currentStage")] string? CurrentStage,
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
        [property: JsonPropertyName("currentStage")] string? CurrentStage,
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
    [McpServerTool(Name = "create_instance"), Description(@"Creates a runtime instance of a domain entity and registers it in the session's instance store.

The instance starts in the first defined stage (if stages exist) and is immediately
available for action execution, policy evaluation, and stage subscriptions.

Use 'invoke_action' to invoke actions on the instance, 'get_instance' to inspect its
current state, 'link_instances' to wire relationship edges between instances for
cross-entity policy evaluation, and 'list_instances' to enumerate all instances.

Thin wrapper around DomainEntityInstance.Create — no new runtime machinery.")]
    public static DomainToolResponse CreateInstance(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity to instantiate")] string entityName,
        [Description("Optional JSON object of initial property values, " +
            "e.g. {\"Name\":\"Alice\",\"Age\":30,\"Status\":\"Active\"}")]
        string? propertiesJson = null) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

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

        // Create instance and register in store
        var instanceId = NewInstanceId();
        DomainEntityInstance instance;
        try {
            instance = DomainEntityInstance.Create(entity, propertyValues, state.Domain);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Failed to create instance: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_entity_detail"]);
        }

        // Register under lock
        var registered = McpSessionStore.TryModifyInstances(sessionId, st => {
            st.InstanceStore ??= new DomainInstanceStore();
            st.InstanceStore.Add(instance);
            st.InstanceMap[instanceId] = instance;
        });

        if (!registered)
            return Failure_NotFound(sessionId);

        var snapshot = BuildSnapshot(instance) with { InstanceId = instanceId };
        return new DomainToolResponse(
            Success: true,
            Message: $"Instance '{instanceId}' of entity '{entityName}' created. Stage: '{instance.CurrentStage ?? "(none)"}'.",
            SessionId: sessionId,
            Data: new { instance = snapshot },
            Affordances: ["get_instance", "invoke_action", "link_instances", "list_instances"]);
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
    public static DomainToolResponse LinkInstances(
        [Description("Session ID")] string sessionId,
        [Description("Instance ID of the source (owning) instance")] string sourceInstanceId,
        [Description("Relationship name as defined in the domain (e.g. 'orders', 'loans')")] string relationshipName,
        [Description("Instance ID of the target (owned) instance")] string targetInstanceId) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        if (!state.InstanceMap.TryGetValue(sourceInstanceId, out var source))
            return new DomainToolResponse(
                Success: false,
                Message: $"Source instance '{sourceInstanceId}' not found.",
                SessionId: sessionId,
                Affordances: ["create_instance", "list_instances"]);

        if (!state.InstanceMap.TryGetValue(targetInstanceId, out var target))
            return new DomainToolResponse(
                Success: false,
                Message: $"Target instance '{targetInstanceId}' not found.",
                SessionId: sessionId,
                Affordances: ["create_instance", "list_instances"]);

        if (state.InstanceStore is null)
            return new DomainToolResponse(
                Success: false,
                Message: "No instance store available. Create at least one instance first.",
                SessionId: sessionId,
                Affordances: ["create_instance"]);

        // Validate relationship exists in domain model and entity ends match.
        // Relationship identity is (source entity, name); the instances disambiguate
        // a name declared on multiple source entities.
        var relationship = (state.LatestAnalysis?.GetAllRelationships(state.Domain) ?? [])
            .FirstOrDefault(r => string.Equals(r.Name, relationshipName, StringComparison.Ordinal)
                && (string.Equals(r.Source.TypeName, source.Entity.Name, StringComparison.Ordinal)
                    || string.Equals(r.Target.TypeName, source.Entity.Name, StringComparison.Ordinal)));
        if (relationship is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Relationship '{relationshipName}' not found in domain '{state.Domain.Name}'. " +
                    $"Available: {string.Join(", ", (state.LatestAnalysis?.GetAllRelationships(state.Domain) ?? []).Select(r => r.Name))}.",
                SessionId: sessionId,
                Affordances: ["get_relationships"]);

        var sourceEntityName = source.Entity.Name;
        var targetEntityName = target.Entity.Name;
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

        try {
            if (!McpSessionStore.TryModifyInstances(sessionId, st => {
                st.InstanceStore!.Link(relationshipName, source, target);
            }))
                return Failure_NotFound(sessionId);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Failed to link instances: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_relationships", "get_entity_detail"]);
        }

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
    public static DomainToolResponse UnlinkInstances(
        [Description("Session ID")] string sessionId,
        [Description("Instance ID of the source (owning) instance")] string sourceInstanceId,
        [Description("Relationship name as defined in the domain (e.g. 'orders', 'loans')")] string relationshipName,
        [Description("Instance ID of the target (owned) instance")] string targetInstanceId) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        if (!state.InstanceMap.TryGetValue(sourceInstanceId, out var source))
            return new DomainToolResponse(
                Success: false,
                Message: $"Source instance '{sourceInstanceId}' not found.",
                SessionId: sessionId,
                Affordances: ["create_instance", "list_instances"]);

        if (!state.InstanceMap.TryGetValue(targetInstanceId, out var target))
            return new DomainToolResponse(
                Success: false,
                Message: $"Target instance '{targetInstanceId}' not found.",
                SessionId: sessionId,
                Affordances: ["create_instance", "list_instances"]);

        if (state.InstanceStore is null)
            return new DomainToolResponse(
                Success: false,
                Message: "No instance store available. Create at least one instance first.",
                SessionId: sessionId,
                Affordances: ["create_instance"]);

        // Validate relationship exists in domain model and entity ends match.
        // Relationship identity is (source entity, name); the instances disambiguate
        // a name declared on multiple source entities.
        var relationship = (state.LatestAnalysis?.GetAllRelationships(state.Domain) ?? [])
            .FirstOrDefault(r => string.Equals(r.Name, relationshipName, StringComparison.Ordinal)
                && (string.Equals(r.Source.TypeName, source.Entity.Name, StringComparison.Ordinal)
                    || string.Equals(r.Target.TypeName, source.Entity.Name, StringComparison.Ordinal)));
        if (relationship is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Relationship '{relationshipName}' not found in domain '{state.Domain.Name}'. " +
                    $"Available: {string.Join(", ", (state.LatestAnalysis?.GetAllRelationships(state.Domain) ?? []).Select(r => r.Name))}.",
                SessionId: sessionId,
                Affordances: ["get_relationships"]);

        var sourceEntityName = source.Entity.Name;
        var targetEntityName = target.Entity.Name;
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
        if (!state.InstanceStore.IsLinked(relationshipName, source, target))
            return new DomainToolResponse(
                Success: false,
                Message: $"No link found between '{sourceInstanceId}' and '{targetInstanceId}' via '{relationshipName}'. " +
                    $"Use link_instances to create the link first.",
                SessionId: sessionId,
                Affordances: ["link_instances", "get_relationships"]);

        try {
            if (!McpSessionStore.TryModifyInstances(sessionId, st => {
                st.InstanceStore!.Unlink(relationshipName, source, target);
            }))
                return Failure_NotFound(sessionId);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Failed to unlink instances: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_relationships", "get_entity_detail"]);
        }

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
    public static DomainToolResponse GetInstance(
        [Description("Session ID")] string sessionId,
        [Description("Instance ID returned by create_instance")] string instanceId) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        if (!state.InstanceMap.TryGetValue(instanceId, out var instance))
            return new DomainToolResponse(
                Success: false,
                Message: $"Instance '{instanceId}' not found.",
                SessionId: sessionId,
                Affordances: ["create_instance", "list_instances"]);

        var snapshot = BuildSnapshot(instance) with { InstanceId = instanceId };

        // Populate navigation links from the instance store (link-3)
        var navs = new List<NavigationLinkData>();
        if (state.InstanceStore is not null) {
            var entityName = instance.Entity.Name;
            foreach (var rel in state.LatestAnalysis?.GetAllRelationships(state.Domain) ?? []) {
                var isSource = string.Equals(rel.Source.TypeName, entityName, StringComparison.Ordinal);
                var isTarget = string.Equals(rel.Target.TypeName, entityName, StringComparison.Ordinal);
                if (!isSource && !isTarget) continue;

                var linked = state.InstanceStore.GetRelatedInstances(rel.Name, instance);
                if (linked.Count == 0) continue;

                var ids = new List<string>(linked.Count);
                foreach (var linkedInstance in linked) {
                    var kvp = state.InstanceMap.FirstOrDefault(
                        kv => ReferenceEquals(kv.Value, linkedInstance));
                    if (kvp.Value is not null)
                        ids.Add(kvp.Key);
                }

                if (ids.Count > 0) {
                    var direction = isSource ? "source→target" : "target→source";
                    navs.Add(new NavigationLinkData(rel.Name, direction, ids));
                }
            }
        }

        snapshot = snapshot with { NavigationLinks = navs };

        return new DomainToolResponse(
            Success: true,
            Message: $"Instance '{instanceId}' of '{instance.Entity.Name}', stage: '{instance.CurrentStage ?? "(none)"}'.",
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
    public static DomainToolResponse ListInstances(
        [Description("Session ID")] string sessionId,
        [Description("Optional entity name filter — only list instances of this entity type")] string? entityName = null) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        var summaries = new List<InstanceSummaryData>();
        foreach (var (id, instance) in state.InstanceMap) {
            if (entityName is not null &&
                !string.Equals(instance.Entity.Name, entityName, StringComparison.Ordinal))
                continue;
            summaries.Add(new InstanceSummaryData(id, instance.Entity.Name,
                instance.CurrentStage, instance.Snapshot().Count));
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
    /// executes effects (transition, assign, create, create-in, link, etc.).
    ///
    /// On success, returns the new stage (if a transition occurred). On failure,
    /// returns which guard policies blocked the action or why the action was not found.
    ///
    /// Stage subscription fan-out happens automatically when a transition occurs:
    /// linked subscriber instances see the transition and their subscription
    /// effects are executed.
    /// </summary>
    [McpServerTool(Name = "invoke_action"), Description(@"Invokes an action on a runtime instance.

The action pipeline:
1. Resolve action from current stage or entity level
2. Evaluate guard policies (action-level, stage-level, entity-level)
3. Execute effects (transition, assign, create, create-in, link/unlink, delete)

On stage transition, linked subscriber instances automatically fire their
stage subscription effects (fan-out via DomainInstanceStore.NotifyTransition).

Returns the result including new stage, any guard failures, and when the action
declares -> EntityType and creates that type, returnTypeName + returnInstanceId.")]
    public static DomainToolResponse InvokeAction(
        [Description("Session ID")] string sessionId,
        [Description("Instance ID returned by create_instance")] string instanceId,
        [Description("Name of the action to invoke")] string actionName,
        [Description("Optional JSON object of action parameter values, " +
            "e.g. {\"amount\":100,\"reason\":\"urgent\"}")]
        string? argsJson = null) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        if (!state.InstanceMap.TryGetValue(instanceId, out var instance))
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

        args = BindEntityTypedActionArgs(state, instance, actionName, args);

        ActionInvocationResult result;
        try {
            result = instance.InvokeAction(actionName, args);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Action execution failed: {ex.Message}",
                SessionId: sessionId,
                Data: new { actionName, error = ex.Message },
                Affordances: ["get_instance", "list_instances"]);
        }

        // Register newly created children in InstanceMap (the invoked instance
        // and any subscriber that ran create-in, e.g. Patron when a Loan goes Overdue).
        string? returnInstanceId = null;
        if (result.Succeeded) {
            var newChildren = new List<DomainEntityInstance>();
            foreach (var owner in state.InstanceMap.Values) {
                foreach (var child in owner.CreatedChildren) {
                    if (state.InstanceMap.Values.Any(v => ReferenceEquals(v, child)))
                        continue;
                    if (newChildren.Any(c => ReferenceEquals(c, child)))
                        continue;
                    newChildren.Add(child);
                }
            }

            if (newChildren.Count > 0) {
                McpSessionStore.TryModifyInstances(sessionId, st => {
                    foreach (var child in newChildren) {
                        var childId = NewInstanceId();
                        st.InstanceStore ??= new DomainInstanceStore();
                        if (child.Store is null)
                            st.InstanceStore.Add(child);
                        st.InstanceMap[childId] = child;
                        if (result.ResultInstance is not null
                            && ReferenceEquals(child, result.ResultInstance))
                            returnInstanceId = childId;
                    }
                });
            }
            else if (result.ResultInstance is not null) {
                // Already registered earlier — find id by reference.
                foreach (var (id, inst) in state.InstanceMap) {
                    if (ReferenceEquals(inst, result.ResultInstance)) {
                        returnInstanceId = id;
                        break;
                    }
                }
            }
        }

        // Re-read map if modify succeeded but local returnInstanceId missed (modify is sync).
        if (returnInstanceId is null && result.ResultInstance is not null
            && McpSessionStore.TryGet(sessionId, out var stateAfter)) {
            foreach (var (id, inst) in stateAfter.InstanceMap) {
                if (ReferenceEquals(inst, result.ResultInstance)) {
                    returnInstanceId = id;
                    break;
                }
            }
        }

        var resultData = new InvokeActionResultData(
            ActionName: result.ActionName,
            Succeeded: result.Succeeded,
            NewStage: result.NewStage,
            FailedGuards: result.FailedGuards.Count > 0 ? result.FailedGuards : null,
            ErrorMessage: result.ErrorMessage,
            ReturnTypeName: result.ResultTypeName,
            ReturnInstanceId: returnInstanceId
        );

        var successMessage = result.Succeeded
            ? returnInstanceId is not null
                ? $"Action '{actionName}' succeeded. Returned '{result.ResultTypeName}' as instance '{returnInstanceId}'. New stage: '{result.NewStage ?? "(unchanged)"}'."
                : $"Action '{actionName}' succeeded. New stage: '{result.NewStage ?? "(unchanged)"}'."
            : (result.ErrorMessage ?? $"Action '{actionName}' blocked by guards: {string.Join(", ", result.FailedGuards)}");

        return new DomainToolResponse(
            Success: result.Succeeded,
            Message: successMessage,
            SessionId: sessionId,
            Data: new { invokeActionResult = resultData },
            Affordances: ["get_instance", "list_instances", "create_instance"]);
    }
}