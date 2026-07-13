using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

using ModelContextProtocol.Server;

using Poly.DomainModeling;
using Poly.DomainModeling.Bootstrap;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Queries;
using Poly.Mcp.Sessions;
using Poly.Syntax.Analysis;

namespace Poly.Mcp.Tools;

// ── Shared response types ──────────────────────────────────────

/// <summary>
/// Response envelope for V3 MCP tool responses.
/// Combines a concise human-readable message with structured data for agents/UI.
/// </summary>
internal sealed record V3Response(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("sessionId")] string? SessionId = null,
    [property: JsonPropertyName("revision")] long? Revision = null,
    [property: JsonPropertyName("data")] object? Data = null,
    [property: JsonPropertyName("diagnostics")] IReadOnlyList<string>? Diagnostics = null,
    [property: JsonPropertyName("affordances")] IReadOnlyList<string>? Affordances = null
);

/// <summary>
/// Structured overview payload for <c>get_domain_overview</c>.
/// </summary>
internal sealed record DomainOverviewData(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("entityCount")] int EntityCount,
    [property: JsonPropertyName("entityNames")] IReadOnlyList<string> EntityNames,
    [property: JsonPropertyName("primitiveCount")] int PrimitiveCount,
    [property: JsonPropertyName("relationshipCount")] int RelationshipCount,
    [property: JsonPropertyName("eventCount")] int EventCount,
    [property: JsonPropertyName("valueTypeCount")] int ValueTypeCount
);

/// <summary>
/// Structured entity detail payload for <c>get_entity_detail</c>.
/// </summary>
internal sealed record EntityDetailData(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("properties")] IReadOnlyList<PropertyData> Properties,
    [property: JsonPropertyName("stages")] IReadOnlyList<StageData> Stages,
    [property: JsonPropertyName("actions")] IReadOnlyList<ActionData> Actions,
    [property: JsonPropertyName("policies")] IReadOnlyList<string> Policies,
    [property: JsonPropertyName("parentEntity")] string? ParentEntityName = null
);

internal sealed record PropertyData(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string TypeName,
    [property: JsonPropertyName("constraintCount")] int ConstraintCount
);

internal sealed record StageData(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("parent")] string? ParentStageName,
    [property: JsonPropertyName("actions")] IReadOnlyList<string> Actions
);

internal sealed record ActionData(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("parameters")] IReadOnlyList<string> Parameters,
    [property: JsonPropertyName("effectCount")] int EffectCount
);

/// <summary>
/// Structured analysis payload for <c>get_domain_analysis</c>.
/// </summary>
internal sealed record AnalysisData(
    [property: JsonPropertyName("errorCount")] int ErrorCount,
    [property: JsonPropertyName("warningCount")] int WarningCount,
    [property: JsonPropertyName("infoCount")] int InfoCount,
    [property: JsonPropertyName("hasStructuralFailure")] bool HasStructuralFailure,
    [property: JsonPropertyName("messages")] IReadOnlyList<string> Messages
);

// ── Tool classes ───────────────────────────────────────────────

/// <summary>
/// Tools for managing V3 domain sessions.
/// </summary>
[McpServerToolType]
internal sealed class V3SessionTool {
    /// <summary>
    /// Creates a new domain session with the canonical built-in primitive types.
    /// The bootstrapped domain includes: Boolean, Number, Text, Date, Time, DateTime, Duration, Uuid, Binary.
    /// Returns a sessionId that must be passed to other tools.
    /// </summary>
    [McpServerTool(Name = "create_domain_session"), Description("Creates a new bootstrapped domain session with built-in primitive types.")]
    public static V3Response CreateDomainSession(
        [Description("Name for the new domain (e.g. 'Orders', 'Inventory')")] string domainName) {
        var (sessionId, state) = McpSessionStore.Create(domainName);
        return new V3Response(
            Success: true,
            Message: $"Domain '{domainName}' created with built-in types.",
            SessionId: sessionId,
            Revision: state.Revision,
            Affordances: ["add_entity", "add_relationship", "get_domain_overview"]
        );
    }

    /// <summary>
    /// Lists all active domain sessions.
    /// </summary>
    [McpServerTool(Name = "list_sessions"), Description("Lists all active domain sessions.")]
    public static V3Response ListSessions() {
        var sessions = McpSessionStore.ListSessions();
        if (sessions.Count == 0)
            return new V3Response(Success: true, Message: "No active sessions.", Affordances: ["create_domain_session"]);

        var ids = string.Join(", ", sessions);
        return new V3Response(
            Success: true,
            Message: sessions.Count == 1
                ? $"1 active session: {ids}"
                : $"{sessions.Count} active sessions: {ids}",
            Data: new { sessionIds = sessions }
        );
    }
}

/// <summary>
/// Tools for querying V3 domain state.
/// </summary>
[McpServerToolType]
internal sealed class V3QueryTool {
    /// <summary>
    /// Returns a high-level overview of the domain: entity/primitive/relationship counts and entity names.
    /// </summary>
    [McpServerTool(Name = "get_domain_overview"), Description("Returns a high-level overview of the domain model (entity/primitive/relationship counts and entity names).")]
    public static V3Response GetDomainOverview(
        [Description("Session ID returned by create_domain_session")] string sessionId) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        var overview = DomainQueries.Overview(state.Domain);
        var entityNames = DomainQueries.ListEntities(state.Domain);
        var data = new DomainOverviewData(
            overview.Name, overview.EntityCount, entityNames,
            overview.PrimitiveTypeCount, overview.RelationshipCount,
            overview.EventCount, overview.ValueTypeCount
        );

        return new V3Response(
            Success: true,
            Message: $"Domain '{overview.Name}': {overview.EntityCount} entities, {overview.PrimitiveTypeCount} primitives, {overview.RelationshipCount} relationships.",
            SessionId: sessionId,
            Revision: state.Revision,
            Data: data,
            Affordances: overview.EntityCount == 0
                ? ["add_entity", "add_relationship"]
                : ["get_entity_detail", "add_entity", "add_property", "add_stage", "add_action"]
        );
    }

    /// <summary>
    /// Returns details about a specific entity: properties, stages, actions, and policies.
    /// </summary>
    [McpServerTool(Name = "get_entity_detail"), Description("Returns detailed information about a specific entity (properties, stages, actions, policies).")]
    public static V3Response GetEntityDetail(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity to inspect")] string entityName) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        var detail = DomainQueries.GetEntity(state.Domain, entityName);
        if (detail is null)
            return new V3Response(
                Success: false,
                Message: $"Entity '{entityName}' not found.",
                SessionId: sessionId,
                Affordances: ["get_domain_overview", "add_entity"]
            );

        var data = new EntityDetailData(
            detail.Name,
            detail.Properties.Select(p => new PropertyData(p.Name, p.TypeName, p.ConstraintCount)).ToList(),
            detail.Stages.Select(s => new StageData(s.Name, s.ParentStageName, s.ActionNames)).ToList(),
            detail.Actions.Select(a => new ActionData(a.Name, a.ParameterNames, a.EffectCount)).ToList(),
            detail.Policies.Select(p => p.Name).ToList(),
            detail.ParentEntityName
        );

        return new V3Response(
            Success: true,
            Message: $"Entity '{entityName}': {detail.Properties.Count} properties, {detail.Stages.Count} stages, {detail.Actions.Count} actions.",
            SessionId: sessionId,
            Revision: state.Revision,
            Data: data,
            Affordances: ["add_property", "add_stage", "add_action", "get_domain_overview"]
        );
    }

    /// <summary>
    /// Returns the domain's analysis diagnostics: error/warning/info counts and the most important messages.
    /// </summary>
    [McpServerTool(Name = "get_domain_analysis"), Description("Returns analysis diagnostics for the current domain state (errors, warnings, info).")]
    public static V3Response GetDomainAnalysis(
        [Description("Session ID")] string sessionId) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        if (state.LatestAnalysis is null)
            return new V3Response(
                Success: false,
                Message: "No analysis available.",
                SessionId: sessionId,
                Affordances: ["get_domain_overview"]
            );

        var summary = DomainQueries.GetAnalysisSummary(state.LatestAnalysis);
        var data = new AnalysisData(
            summary.ErrorCount, summary.WarningCount, summary.InfoCount,
            summary.HasStructuralFailure, summary.Messages
        );

        var message = summary.ErrorCount > 0
            ? $"{summary.ErrorCount} error(s), {summary.WarningCount} warning(s). See diagnostics for details."
            : $"{summary.InfoCount} info(s), {summary.WarningCount} warning(s). No errors.";

        return new V3Response(
            Success: true,
            Message: message,
            SessionId: sessionId,
            Revision: state.Revision,
            Data: data,
            Diagnostics: summary.Messages.Count > 0 ? summary.Messages : null,
            Affordances: summary.ErrorCount > 0 && summary.HasStructuralFailure
                ? ["get_domain_overview"]
                : null
        );
    }

    private static V3Response Failure_NotFound(string sessionId) =>
        new(Success: false, Message: $"Session '{sessionId}' not found.",
            Affordances: ["create_domain_session", "list_sessions"]);
}

/// <summary>
/// Tools for evolving (mutating) a V3 domain through the analysis-gated evolution pipeline.
/// Each tool resolves the session, applies the change, gates on analysis, and updates the session on success.
/// On rollback, the response includes diagnostics and affordances.
/// </summary>
[McpServerToolType]
internal sealed class V3EvolveTool {
    /// <summary>
    /// Adds a new entity type to the domain.
    /// </summary>
    [McpServerTool(Name = "add_entity"), Description("Adds a new entity type to the domain. The entity starts with no properties, stages, or actions.")]
    public static V3Response AddEntity(
        [Description("Session ID")] string sessionId,
        [Description("Name of the new entity (e.g. 'Order', 'Customer')")] string entityName) {
        return Evolve(sessionId, builder => builder.AddEntity(entityName),
            successAffordances: ["add_property", "add_stage", "add_action", "add_relationship"]);
    }

    /// <summary>
    /// Adds a property to an existing entity.
    /// </summary>
    [McpServerTool(Name = "add_property"), Description("Adds a property to an existing entity. The type must be a built-in primitive (Text, Number, Boolean, DateTime, etc.) or another known type.")]
    public static V3Response AddProperty(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity to add the property to")] string entityName,
        [Description("Name of the new property")] string propertyName,
        [Description("Type of the property (e.g. 'Text', 'Number', 'Boolean', 'DateTime')")] string typeName) {
        return Evolve(sessionId, builder =>
                builder.AddPropertyToEntity(entityName, new Property(propertyName, new DomainTypeReference(typeName), [])),
            successAffordances: ["add_property", "add_stage", "add_action", "get_entity_detail"]);
    }

    /// <summary>
    /// Adds a lifecycle stage to an entity.
    /// </summary>
    [McpServerTool(Name = "add_stage"), Description("Adds a lifecycle stage to an entity. Optionally specify a parent stage for stage hierarchies.")]
    public static V3Response AddStage(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity")] string entityName,
        [Description("Name of the new stage (e.g. 'Draft', 'Active', 'Archived')")] string stageName,
        [Description("Optional parent stage name for stage hierarchy")] string? parentStageName = null) {
        return Evolve(sessionId, builder => {
            return parentStageName is not null
                ? builder.AddStage(entityName, stageName, parentStageName)
                : builder.AddStage(entityName, stageName);
        },
            successAffordances: ["add_action", "add_action_to_stage", "add_property", "get_entity_detail"]);
    }

    /// <summary>
    /// Adds an action/operation to an entity.
    /// </summary>
    [McpServerTool(Name = "add_action"), Description("Adds an action/operation to an entity. Actions model behaviors like 'Submit', 'Approve', 'Cancel'.")]
    public static V3Response AddAction(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity")] string entityName,
        [Description("Name of the new action (e.g. 'Submit', 'Approve', 'Cancel')")] string actionName) {
        return Evolve(sessionId, builder => builder.AddAction(entityName, actionName),
            successAffordances: ["add_action_to_stage", "add_property", "get_entity_detail"]);
    }

    /// <summary>
    /// Creates a new action on a stage. The action is placed directly on the stage
    /// and available only within that stage's lifecycle.
    /// </summary>
    [McpServerTool(Name = "add_action_to_stage"), Description("Creates a new action on a stage. The action is placed directly on the stage and available only within that stage's lifecycle.")]
    public static V3Response AddActionToStage(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity")] string entityName,
        [Description("Name of the stage")] string stageName,
        [Description("Name of the action")] string actionName) {
        return Evolve(sessionId, builder => builder.AddActionToStage(entityName, stageName, actionName),
            successAffordances: ["add_action", "add_property", "add_stage", "get_entity_detail"]);
    }

    /// <summary>
    /// Adds a relationship between two entity types.
    /// </summary>
    [McpServerTool(Name = "add_relationship"), Description("Adds a relationship between two entity types. Cardinality options: OneToOne, OneToMany, ManyToMany.")]
    public static V3Response AddRelationship(
        [Description("Session ID")] string sessionId,
        [Description("Name of the relationship")] string relationshipName,
        [Description("Source entity name")] string sourceEntityName,
        [Description("Target entity name")] string targetEntityName,
        [Description("Cardinality: OneToOne, OneToMany, ManyToMany")] string cardinality = "OneToMany",
        [Description("Whether the source entity owns the target")] bool sourceOwnsTarget = false) {
        var card = cardinality switch {
            "OneToOne" => RelationshipCardinality.OneToOne,
            "OneToMany" => RelationshipCardinality.OneToMany,
            "ManyToMany" => RelationshipCardinality.ManyToMany,
            _ => RelationshipCardinality.OneToMany
        };

        return Evolve(sessionId, builder =>
                builder.AddRelationship(relationshipName, sourceEntityName, targetEntityName, card, sourceOwnsTarget),
            successAffordances: ["add_relationship", "get_entity_detail", "get_domain_overview"]);
    }

    // ── Shared helpers ──────────────────────────────────────────

    /// <summary>
    /// Builds a structural fingerprint of a domain for no-op detection.
    /// Two domains with the same fingerprint have the same types, relationships,
    /// and entity structures (property/stage/action counts). This lets us detect
    /// when an evolve operation had zero effective change (e.g. adding a property
    /// to a non-existent entity, which silently no-ops in the current evolution layer).
    /// </summary>
    internal static string GetFingerprint(Domain domain) {
        var typeCounts = $"T:{domain.Types.Count}|R:{domain.Relationships.Count}";
        var entityDetails = domain.Types
            .OfType<Entity>()
            .OrderBy(e => e.Name)
            .Select(e => {
                var props = $"P{e.Properties.Count}";
                var stages = string.Join(",", e.Stages.OrderBy(s => s.Name)
                    .Select(s => $"{s.Name}({s.Actions.Count}a)"));
                var actions = $"A{e.Actions.Count}";
                var stageActions = e.Stages.Sum(s => s.Actions.Count);
                return $"{e.Name}:{props}|[{stages}]|{actions}(+{stageActions}sa)";
            });
        return entityDetails.Any()
            ? $"{typeCounts}|{string.Join(",", entityDetails)}"
            : typeCounts;
    }

    private static V3Response Evolve(
        string sessionId,
        Func<EvolutionBuilder, EvolutionBuilder> mutate,
        IReadOnlyList<string>? successAffordances = null) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return new V3Response(
                Success: false,
                Message: $"Session '{sessionId}' not found.",
                Affordances: ["create_domain_session", "list_sessions"]);

        // Snapshot fingerprint before mutation to detect silent no-ops
        var before = GetFingerprint(state.Domain);

        var result = new DomainEvolution(state.Domain).Evolve();
        result = mutate(result);
        var outcome = result.Apply();

        if (!outcome.Succeeded) {
            // Build enriched diagnostics from the analysis result
            var diagnostics = outcome.FailureSummary is not null
                ? new List<string> { outcome.FailureSummary }
                : new List<string>();

            // Add up to 3 additional error messages for context
            var errorMessages = outcome.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(3)
                .Select(d => d.Message)
                .ToList();
            diagnostics.AddRange(errorMessages);

            return new V3Response(
                Success: false,
                Message: $"Evolution rolled back: {outcome.FailureSummary ?? "Analysis failed"}",
                SessionId: sessionId,
                Revision: state.Revision,
                Diagnostics: diagnostics.Count > 0 ? diagnostics : null,
                Affordances: ["get_domain_analysis", "get_domain_overview"]
            );
        }

        // Guard: detect silent no-ops — evolution "succeeded" but nothing changed.
        var after = GetFingerprint(outcome.Root);
        if (before == after) {
            return new V3Response(
                Success: false,
                Message: "No changes applied: target entity not found or change had no effect. Check that the entity name is correct.",
                SessionId: sessionId,
                Revision: state.Revision,
                Affordances: ["get_domain_overview", "get_entity_detail"]
            );
        }

        McpSessionStore.Update(sessionId, outcome.Root, outcome.Analysis);
        return new V3Response(
            Success: true,
            Message: "Change applied successfully.",
            SessionId: sessionId,
            Revision: state.Revision + 1,
            Affordances: successAffordances
        );
    }
}

/// <summary>
/// Tools for inspecting policy metadata (guard expressions) on domain entities.
/// Full VM-based policy evaluation with record-building from JSON args is deferred
/// to WP5 — see `docs/plans/v2-to-v3/workstreams/ws8-analysis-unification-and-lowering.md`.
/// </summary>
[McpServerToolType]
internal sealed class V3EvalTool {
    /// <summary>
    /// Returns the guard expression text of a named policy on an entity.
    /// Use this to inspect what condition a policy enforces (e.g. "Age >= 18").
    /// </summary>
    [McpServerTool(Name = "get_policy_expression"), Description("Returns the guard expression text of a named policy on an entity for inspection.")]
    public static V3Response GetPolicyExpression(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity that has the policy")] string entityName,
        [Description("Name of the policy to inspect")] string policyName) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return new V3Response(
                Success: false,
                Message: $"Session '{sessionId}' not found.",
                Affordances: ["create_domain_session", "list_sessions"]);

        var entity = state.Domain.Types.OfType<Entity>()
            .FirstOrDefault(e => string.Equals(e.Name, entityName, StringComparison.Ordinal));
        if (entity is null)
            return new V3Response(
                Success: false,
                Message: $"Entity '{entityName}' not found.",
                SessionId: sessionId,
                Affordances: ["get_domain_overview", "add_entity"]);

        var policy = entity.Policies
            .FirstOrDefault(p => string.Equals(p.Name, policyName, StringComparison.Ordinal));
        if (policy is null)
            return new V3Response(
                Success: false,
                Message: $"Policy '{policyName}' not found on entity '{entityName}'.",
                SessionId: sessionId,
                Affordances: ["get_entity_detail"]);

        return new V3Response(
            Success: true,
            Message: $"Policy '{policyName}' on '{entityName}': {policy.Expression}",
            SessionId: sessionId,
            Revision: state.Revision,
            Data: new { policyName = policy.Name, entityName = entity.Name, expression = policy.Expression.ToString() },
            Affordances: ["get_entity_detail", "get_domain_overview"]
        );
    }

    /// <summary>
    /// Adds a policy with a simple property comparison guard to an entity.
    /// For composite expressions (AND/OR/NOT), use the structured contract fields.
    /// </summary>
    [McpServerTool(Name = "add_policy"), Description("Adds a policy with a guard expression to an entity. Simple comparison: provide property, op (==, !=, >, >=, <, <=), and value. Composite: provide 'and', 'or', or 'not' with sub-expressions.")]
    public static V3Response AddPolicy(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity")] string entityName,
        [Description("Policy name (e.g. 'Adult', 'LargeActive')")] string policyName,
        [Description("Property name for comparison (e.g. 'Age', 'Total'). Required for property comparisons.")] string? property = null,
        [Description("Comparison operator: ==, !=, >, >=, <, <=")] string? op = null,
        [Description("Literal value for comparison or standalone literal")] object? value = null,
        [Description("JSON string for composite 'and' sub-expressions, e.g. [{\"property\":\"A\",\"op\":\">=\",\"value\":1},{\"property\":\"B\",\"op\":\"<\",\"value\":5}]")] string? and = null,
        [Description("JSON string for composite 'or' sub-expressions")] string? or = null,
        [Description("JSON string for 'not' sub-expression, e.g. {\"property\":\"X\",\"op\":\">=\",\"value\":18}")] string? not = null) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return new V3Response(
                Success: false,
                Message: $"Session '{sessionId}' not found.",
                Affordances: ["create_domain_session", "list_sessions"]);

        // Build contract from tool arguments
        PolicyExpressionContract contract;
        try {
            contract = BuildContract(property, op, value, and, or, not);
        }
        catch (Exception ex) {
            return new V3Response(
                Success: false,
                Message: $"Invalid policy expression: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_entity_detail", "get_domain_overview"]);
        }

        DomainExpression expression;
        try {
            expression = PolicyExpressionParser.Parse(contract);
        }
        catch (Exception ex) {
            return new V3Response(
                Success: false,
                Message: $"Invalid policy expression: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_entity_detail", "get_domain_overview"]);
        }

        // Evolve — evolution gate handles rollback; fingerprint not needed
        // since AddPolicyToEntity fails loud when entity is missing (S0.1).
        var result = new DomainEvolution(state.Domain).Evolve()
            .AddPolicyToEntity(entityName, policyName, expression)
            .Apply();

        if (!result.Succeeded) {
            return new V3Response(
                Success: false,
                Message: $"Evolution rolled back: {result.FailureSummary ?? "Analysis failed"}",
                SessionId: sessionId,
                Revision: state.Revision,
                Affordances: ["get_domain_analysis", "get_domain_overview"]);
        }

        McpSessionStore.Update(sessionId, result.Root, result.Analysis);
        return new V3Response(
            Success: true,
            Message: $"Policy '{policyName}' added to entity '{entityName}'.",
            SessionId: sessionId,
            Revision: state.Revision + 1,
            Affordances: ["get_entity_detail", "get_policy_expression"]);
    }

    /// <summary>
    /// Evaluates a policy's guard expression against a sample subject.
    /// Provide <c>age</c> for simple Age-based policies, or <c>properties</c>
    /// as a JSON object for multi-property entities (e.g. {"Status":"Active","Total":200}).
    /// Returns the boolean result (true/false) from the VM.
    /// </summary>
    [McpServerTool(Name = "evaluate_policy"), Description("Evaluates a policy's guard expression against a sample subject. Provide 'age' for simple Age-based policies, or 'properties' as a JSON object for multi-property entities. Returns true if the policy passes, false otherwise.")]
    public static V3Response EvaluatePolicy(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity that has the policy")] string entityName,
        [Description("Name of the policy to evaluate")] string policyName,
        [Description("Age value for the sample subject (convenience for Age-based policies)")] int? age = null,
        [Description("JSON object of property values, e.g. \"{\\\"Status\\\":\\\"Active\\\",\\\"Total\\\":200}\"")] string? properties = null) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return new V3Response(
                Success: false,
                Message: $"Session '{sessionId}' not found.",
                Affordances: ["create_domain_session", "list_sessions"]);

        var entity = state.Domain.Types.OfType<Entity>()
            .FirstOrDefault(e => string.Equals(e.Name, entityName, StringComparison.Ordinal));
        if (entity is null)
            return new V3Response(
                Success: false,
                Message: $"Entity '{entityName}' not found.",
                SessionId: sessionId,
                Affordances: ["get_domain_overview", "add_entity"]);

        var policy = entity.Policies
            .FirstOrDefault(p => string.Equals(p.Name, policyName, StringComparison.Ordinal));
        if (policy is null)
            return new V3Response(
                Success: false,
                Message: $"Policy '{policyName}' not found on entity '{entityName}'.",
                SessionId: sessionId,
                Affordances: ["get_entity_detail"]);

        // Parse subject values from tool arguments.
        Dictionary<string, object?> subjectValues;
        try {
            subjectValues = new Dictionary<string, object?>(StringComparer.Ordinal);

            if (properties is not null) {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(properties)
                    ?? throw new ArgumentException("Failed to parse properties JSON.");
                foreach (var (key, je) in parsed)
                    subjectValues[key] = JsonElementToClrValue(je);
            }
            else if (age.HasValue) {
                subjectValues["Age"] = (long)age.Value;
            }
        }
        catch (Exception ex) {
            return new V3Response(
                Success: false,
                Message: $"Invalid subject: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_entity_detail", "get_domain_overview"]);
        }

        bool result;
        try {
            var instance = DomainEntityInstance.Create(entity, subjectValues);
            result = instance.EvaluatePolicy(policy);
        }
        catch (Exception ex) {
            return new V3Response(
                Success: false,
                Message: $"Evaluation failed: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_policy_expression", "get_entity_detail"]);
        }

        var data = new { policyName, entityName, age, properties, result };

        return new V3Response(
            Success: true,
            Message: result ? "Policy passed (true)." : "Policy failed (false).",
            SessionId: sessionId,
            Revision: state.Revision,
            Data: data,
            Affordances: ["get_policy_expression", "get_entity_detail"]);
    }

    // ── Private helpers ─────────────────────────────────────────

    private static PolicyExpressionContract BuildContract(
        string? property, string? op, object? value,
        string? and, string? or, string? not) {
        if (and is not null)
            return new PolicyExpressionContract { And = ParseContractArray(and) };
        if (or is not null)
            return new PolicyExpressionContract { Or = ParseContractArray(or) };
        if (not is not null)
            return new PolicyExpressionContract { Not = ParseContractSingle(not) };
        if (property is not null)
            return new PolicyExpressionContract { Property = property, Op = op, Value = value };
        throw new ArgumentException("No expression provided. Specify property+op+value, or a composite (and/or/not).");
    }

    private static PolicyExpressionContract[] ParseContractArray(string json) {
        var arr = System.Text.Json.JsonSerializer.Deserialize<PolicyExpressionContract[]>(json, _contractOptions);
        if (arr is null) throw new ArgumentException("Failed to parse composite expression array.");
        return arr;
    }

    private static PolicyExpressionContract ParseContractSingle(string json) {
        var obj = System.Text.Json.JsonSerializer.Deserialize<PolicyExpressionContract>(json, _contractOptions);
        if (obj is null) throw new ArgumentException("Failed to parse composite expression.");
        return obj;
    }

    private static readonly System.Text.Json.JsonSerializerOptions _contractOptions = new() {
        Converters = { new JsonValueNormalizingConverter() }
    };

    /// <summary>
    /// Custom JSON converter that recursively converts <see cref="JsonElement"/> values
    /// in <see cref="PolicyExpressionContract.Value"/> to primitive types (int, bool, string).
    /// Without this, JSON-deserialized contracts store <c>object?</c> as <c>JsonElement</c>
    /// which the VM's <c>TryValueToLong</c> cannot handle.
    /// </summary>
    private sealed class JsonValueNormalizingConverter : System.Text.Json.Serialization.JsonConverter<PolicyExpressionContract> {
        public override PolicyExpressionContract? Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) {
            // Use a custom options that doesn't include this converter recursively to avoid infinite recursion
            // for nested And/Or/Not properties
            var doc = System.Text.Json.JsonDocument.ParseValue(ref reader);
            return ParseContractFromElement(doc.RootElement);
        }

        public override void Write(System.Text.Json.Utf8JsonWriter writer, PolicyExpressionContract value, System.Text.Json.JsonSerializerOptions options) {
            System.Text.Json.JsonSerializer.Serialize(writer, value);
        }

        private static PolicyExpressionContract ParseContractFromElement(System.Text.Json.JsonElement element) {
            var result = new PolicyExpressionContract();

            if (element.TryGetProperty("property", out var prop))
                result = result with { Property = prop.GetString() };
            if (element.TryGetProperty("op", out var op))
                result = result with { Op = op.GetString() };
            if (element.TryGetProperty("value", out var val))
                result = result with { Value = JsonElementToPrimitive(val) };
            if (element.TryGetProperty("literal", out var lit))
                result = result with { Literal = JsonElementToPrimitive(lit) };

            if (element.TryGetProperty("and", out var andEl) && andEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                result = result with { And = andEl.EnumerateArray().Select(ParseContractFromElement).ToArray() };
            if (element.TryGetProperty("or", out var orEl) && orEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                result = result with { Or = orEl.EnumerateArray().Select(ParseContractFromElement).ToArray() };
            if (element.TryGetProperty("not", out var notEl) && notEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                result = result with { Not = ParseContractFromElement(notEl) };

            return result;
        }
    }

    private static object? JsonElementToPrimitive(System.Text.Json.JsonElement je) => je.ValueKind switch {
        System.Text.Json.JsonValueKind.Number when je.TryGetInt32(out var i) => i,
        System.Text.Json.JsonValueKind.Number when je.TryGetInt64(out var l) => l,
        System.Text.Json.JsonValueKind.Number => je.GetDecimal(),
        System.Text.Json.JsonValueKind.True => true,
        System.Text.Json.JsonValueKind.False => false,
        System.Text.Json.JsonValueKind.String => je.GetString(),
        System.Text.Json.JsonValueKind.Null => null,
        _ => je.GetRawText()
    };

    /// <summary>
    /// Converts a JSON element to a CLR value for subject bag properties.
    /// </summary>
    private static object? JsonElementToClrValue(System.Text.Json.JsonElement je) => je.ValueKind switch {
        System.Text.Json.JsonValueKind.Number when je.TryGetInt32(out var i) => (long)i,
        System.Text.Json.JsonValueKind.Number when je.TryGetInt64(out var l) => l,
        System.Text.Json.JsonValueKind.Number => (long)je.GetDecimal(),
        System.Text.Json.JsonValueKind.True => true,
        System.Text.Json.JsonValueKind.False => false,
        System.Text.Json.JsonValueKind.String => je.GetString(),
        System.Text.Json.JsonValueKind.Null => null,
        _ => je.GetRawText()
    };
}