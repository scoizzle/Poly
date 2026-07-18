using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

using ModelContextProtocol.Server;

using Poly.DomainModeling;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Queries;
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
            IsDeleted: instance.IsDeleted,
            CreatedChildCount: instance.CreatedChildren.Count
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
        [property: JsonPropertyName("isDeleted")] bool IsDeleted,
        [property: JsonPropertyName("createdChildCount")] int CreatedChildCount
    );

    internal sealed record InstanceSummaryData(
        [property: JsonPropertyName("instanceId")] string InstanceId,
        [property: JsonPropertyName("entityName")] string EntityName,
        [property: JsonPropertyName("currentStage")] string? CurrentStage,
        [property: JsonPropertyName("propertyCount")] int PropertyCount
    );

    internal sealed record CallActionResultData(
        [property: JsonPropertyName("actionName")] string ActionName,
        [property: JsonPropertyName("succeeded")] bool Succeeded,
        [property: JsonPropertyName("newStage")] string? NewStage,
        [property: JsonPropertyName("failedGuards")] IReadOnlyList<string>? FailedGuards,
        [property: JsonPropertyName("errorMessage")] string? ErrorMessage
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

Use 'call_action' to invoke actions on the instance, 'get_instance' to inspect its
current state, and 'list_instances' to enumerate all instances in the session.

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
                Affordances: ["get_domain_overview", "add_entity"]);

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
            Affordances: ["get_instance", "call_action", "list_instances"]);
    }

    // ── RT.1: get_instance ─────────────────────────────────────

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
        return new DomainToolResponse(
            Success: true,
            Message: $"Instance '{instanceId}' of '{instance.Entity.Name}', stage: '{instance.CurrentStage ?? "(none)"}'.",
            SessionId: sessionId,
            Data: new { instance = snapshot },
            Affordances: ["call_action", "list_instances", "create_instance"]);
    }

    // ── RT.1: list_instances ───────────────────────────────────

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
            if (instance.IsDeleted) continue;
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
                ? ["get_instance", "call_action", "create_instance"]
                : ["create_instance"]);
    }

    // ── RT.2: call_action ──────────────────────────────────────

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
    [McpServerTool(Name = "call_action"), Description(@"Calls an action on a runtime instance.

The action pipeline:
1. Resolve action from current stage or entity level
2. Evaluate guard policies (action-level, stage-level, entity-level)
3. Execute effects (transition, assign, create, create-in, link/unlink, delete)

On stage transition, linked subscriber instances automatically fire their
stage subscription effects (fan-out via DomainInstanceStore.NotifyTransition).

Returns the result including new stage and any guard failures.")]
    public static DomainToolResponse CallAction(
        [Description("Session ID")] string sessionId,
        [Description("Instance ID returned by create_instance")] string instanceId,
        [Description("Name of the action to invoke")] string actionName) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        if (!state.InstanceMap.TryGetValue(instanceId, out var instance))
            return new DomainToolResponse(
                Success: false,
                Message: $"Instance '{instanceId}' not found.",
                SessionId: sessionId,
                Affordances: ["create_instance", "list_instances"]);

        if (instance.IsDeleted)
            return new DomainToolResponse(
                Success: false,
                Message: $"Instance '{instanceId}' has been deleted.",
                SessionId: sessionId,
                Affordances: ["create_instance"]);

        ActionCallResult result;
        try {
            result = instance.CallAction(actionName);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Action execution failed: {ex.Message}",
                SessionId: sessionId,
                Data: new { actionName, error = ex.Message },
                Affordances: ["get_instance", "list_instances"]);
        }

        var resultData = new CallActionResultData(
            ActionName: result.ActionName,
            Succeeded: result.Succeeded,
            NewStage: result.NewStage,
            FailedGuards: result.FailedGuards.Count > 0 ? result.FailedGuards : null,
            ErrorMessage: result.ErrorMessage
        );

        return new DomainToolResponse(
            Success: result.Succeeded,
            Message: result.Succeeded
                ? $"Action '{actionName}' succeeded. New stage: '{result.NewStage ?? "(unchanged)"}'."
                : (result.ErrorMessage ?? $"Action '{actionName}' blocked by guards: {string.Join(", ", result.FailedGuards)}"),
            SessionId: sessionId,
            Data: new { callActionResult = resultData },
            Affordances: ["get_instance", "list_instances", "create_instance"]);
    }
}