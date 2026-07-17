using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

using ModelContextProtocol.Server;

using Poly.DomainModeling;
using Poly.DomainModeling.Bootstrap;
using Poly.DomainModeling.Constraints;
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
internal sealed record DomainToolResponse(
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

internal sealed record SubscriptionData(
    [property: JsonPropertyName("relationshipName")] string RelationshipName,
    [property: JsonPropertyName("stageNames")] IReadOnlyList<string> StageNames,
    [property: JsonPropertyName("quantifier")] string Quantifier,
    [property: JsonPropertyName("effectCount")] int EffectCount
);

internal sealed record StageData(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("parent")] string? ParentStageName,
    [property: JsonPropertyName("actions")] IReadOnlyList<string> Actions,
    [property: JsonPropertyName("subscriptions")] IReadOnlyList<SubscriptionData> Subscriptions
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
internal sealed class SessionTool {
    /// <summary>
    /// Creates a new domain session with the canonical built-in primitive types.
    /// The bootstrapped domain includes: Boolean, Number, Text, Date, Time, DateTime, Duration, Uuid, Binary.
    /// Returns a sessionId that must be passed to other tools.
    /// </summary>
    [McpServerTool(Name = "create_domain_session"), Description("Creates a new bootstrapped domain session with built-in primitive types.")]
    public static DomainToolResponse CreateDomainSession(
        [Description("Name for the new domain (e.g. 'Orders', 'Inventory')")] string domainName) {
        var (sessionId, state) = McpSessionStore.Create(domainName);
        return new DomainToolResponse(
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
    public static DomainToolResponse ListSessions() {
        var sessions = McpSessionStore.ListSessions();
        if (sessions.Count == 0)
            return new DomainToolResponse(Success: true, Message: "No active sessions.", Affordances: ["create_domain_session"]);

        var ids = string.Join(", ", sessions);
        return new DomainToolResponse(
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
internal sealed class QueryTool {
    /// <summary>
    /// Returns a high-level overview of the domain: entity/primitive/relationship counts and entity names.
    /// </summary>
    [McpServerTool(Name = "get_domain_overview"), Description("Returns a high-level overview of the domain model (entity/primitive/relationship counts and entity names).")]
    public static DomainToolResponse GetDomainOverview(
        [Description("Session ID returned by create_domain_session")] string sessionId) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        var overview = DomainQueries.Overview(state.Domain);
        var entityNames = DomainQueries.ListEntities(state.Domain);
        var data = new DomainOverviewData(
            overview.Name, overview.EntityCount, entityNames,
            overview.PrimitiveTypeCount, overview.RelationshipCount,
            overview.ValueTypeCount
        );

        return new DomainToolResponse(
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
    public static DomainToolResponse GetEntityDetail(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity to inspect")] string entityName) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        var detail = DomainQueries.GetEntity(state.Domain, entityName);
        if (detail is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Entity '{entityName}' not found.",
                SessionId: sessionId,
                Affordances: ["get_domain_overview", "add_entity"]
            );

        var data = new EntityDetailData(
            detail.Name,
            detail.Properties.Select(p => new PropertyData(p.Name, p.TypeName, p.ConstraintCount)).ToList(),
            detail.Stages.Select(s => new StageData(
                s.Name, s.ParentStageName, s.ActionNames,
                s.Subscriptions.Select(sub => new SubscriptionData(
                    sub.RelationshipName, sub.StageNames, sub.Quantifier, sub.EffectCount)).ToList())).ToList(),
            detail.Actions.Select(a => new ActionData(a.Name, a.ParameterNames, a.EffectCount)).ToList(),
            detail.Policies.Select(p => p.Name).ToList(),
            detail.ParentEntityName
        );

        return new DomainToolResponse(
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
    public static DomainToolResponse GetDomainAnalysis(
        [Description("Session ID")] string sessionId) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        if (state.LatestAnalysis is null)
            return new DomainToolResponse(
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

        return new DomainToolResponse(
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

    /// <summary>
    /// Returns a complete snapshot of the domain: all entities with full detail
    /// (properties, stages, actions, policies) plus relationships and analysis.
    /// </summary>
    [McpServerTool(Name = "get_domain_snapshot"), Description("Returns a complete snapshot of the domain model: all entities with full detail, relationships, and analysis diagnostics.")]
    public static DomainToolResponse GetDomainSnapshot(
        [Description("Session ID")] string sessionId) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        var entities = state.Domain.Types.OfType<Entity>().Select(e => {
            var detail = DomainQueries.GetEntity(state.Domain, e.Name)!;
            return new {
                name = e.Name,
                properties = detail.Properties.Select(p => new { p.Name, p.TypeName, p.ConstraintCount }),
                stages = detail.Stages.Select(s => new { s.Name, parent = s.ParentStageName, actions = s.ActionNames }),
                actions = detail.Actions.Select(a => new { a.Name, a.ParameterNames, a.EffectCount }),
                policies = detail.Policies.Select(p => p.Name)
            };
        }).ToList();

        var relationships = DomainQueries.ListRelationships(state.Domain);

        var data = new {
            domainName = state.Domain.Name,
            revision = state.Revision,
            primitiveTypes = state.Domain.Types.OfType<PrimitiveType>().Select(p => p.Name).ToList(),
            entities,
            relationships,
            analysis = state.LatestAnalysis is not null
                ? new {
                    errors = state.LatestAnalysis.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error),
                    warnings = state.LatestAnalysis.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning)
                }
                : null
        };

        return new DomainToolResponse(
            Success: true,
            Message: $"Snapshot of '{state.Domain.Name}': {entities.Count} entities, {relationships.Count} relationships.",
            SessionId: sessionId,
            Revision: state.Revision,
            Data: data,
            Affordances: ["get_entity_detail", "get_domain_analysis"]
        );
    }

    /// <summary>
    /// Lists all relationships in the domain, optionally filtered by entity name.
    /// </summary>
    [McpServerTool(Name = "get_relationships"), Description("Lists all relationships in the domain. Optionally filter by entity name to show relationships where that entity is source or target.")]
    public static DomainToolResponse GetRelationships(
        [Description("Session ID")] string sessionId,
        [Description("Optional entity name filter — only show relationships involving this entity")] string? entityName = null) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        var all = DomainQueries.ListRelationships(state.Domain);

        var filtered = entityName is not null
            ? all.Where(r => string.Equals(r.SourceEntityName, entityName, StringComparison.Ordinal)
                          || string.Equals(r.TargetEntityName, entityName, StringComparison.Ordinal)).ToList()
            : all;

        return new DomainToolResponse(
            Success: true,
            Message: $"{filtered.Count} relationship(s)" + (entityName is not null ? $" involving '{entityName}'." : "."),
            SessionId: sessionId,
            Revision: state.Revision,
            Data: filtered,
            Affordances: ["add_relationship", "get_entity_detail"]
        );
    }

    private static DomainToolResponse Failure_NotFound(string sessionId) =>
        new(Success: false, Message: $"Session '{sessionId}' not found.",
            Affordances: ["create_domain_session", "list_sessions"]);
}

/// <summary>
/// Tools for evolving (mutating) a V3 domain through the analysis-gated evolution pipeline.
/// Each tool resolves the session, applies the change, gates on analysis, and updates the session on success.
/// On rollback, the response includes diagnostics and affordances.
/// </summary>
[McpServerToolType]
internal sealed class EvolveTool {
    /// <summary>
    /// Adds a new entity type to the domain.
    /// </summary>
    [McpServerTool(Name = "add_entity"), Description("Adds a new entity type to the domain. The entity starts with no properties, stages, or actions.")]
    public static DomainToolResponse AddEntity(
        [Description("Session ID")] string sessionId,
        [Description("Name of the new entity (e.g. 'Order', 'Customer')")] string entityName) {
        return Evolve(sessionId, builder => builder.AddEntity(entityName),
            successAffordances: ["add_property", "add_stage", "add_action", "add_relationship"]);
    }

    /// <summary>
    /// Adds a property to an existing entity.
    /// </summary>
    [McpServerTool(Name = "add_property"), Description("Adds a property to an existing entity. The type must be a built-in primitive (Text, Number, Boolean, DateTime, etc.) or another known type.")]
    public static DomainToolResponse AddProperty(
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
    public static DomainToolResponse AddStage(
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
    public static DomainToolResponse AddAction(
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
    public static DomainToolResponse AddActionToStage(
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
    public static DomainToolResponse AddRelationship(
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

    // ── Batch/plural tools ──────────────────────────────────────

    /// <summary>
    /// Adds multiple properties to an entity in a single atomic batch.
    /// </summary>
    [McpServerTool(Name = "add_properties"), Description("Adds multiple properties to an entity in a single atomic batch. Provide a JSON array of {name, typeName} objects.")]
    public static DomainToolResponse AddProperties(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity")] string entityName,
        [Description("JSON array of property objects: [{\"name\":\"Age\",\"typeName\":\"Number\"},...]")] string properties) {
        PropertySpec[] specs;
        try {
            specs = JsonSerializer.Deserialize<PropertySpec[]>(properties)
                ?? throw new ArgumentException("Properties array must not be null.");
            if (specs.Length == 0)
                throw new ArgumentException("Properties array must not be empty.");
        }
        catch (Exception ex) when (ex is not ArgumentException) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Invalid properties JSON: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_entity_detail"]);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: ex.Message,
                SessionId: sessionId,
                Affordances: ["get_entity_detail"]);
        }

        return Evolve(sessionId, builder => {
            foreach (var s in specs)
                builder = builder.AddPropertyToEntity(entityName,
                    new Property(s.Name, new DomainTypeReference(s.TypeName), []));
            return builder;
        }, successAffordances: ["add_property", "add_stage", "get_entity_detail"]);
    }

    /// <summary>
    /// Adds multiple lifecycle stages to an entity in a single atomic batch.
    /// </summary>
    [McpServerTool(Name = "add_stages"), Description("Adds multiple lifecycle stages to an entity in a single atomic batch. Provide a JSON array of {name, parentStageName?} objects.")]
    public static DomainToolResponse AddStages(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity")] string entityName,
        [Description("JSON array of stage objects: [{\"name\":\"Draft\"},{\"name\":\"Review\",\"parentStageName\":\"Draft\"},...]")] string stages) {
        StageSpec[] specs;
        try {
            specs = JsonSerializer.Deserialize<StageSpec[]>(stages)
                ?? throw new ArgumentException("Stages array must not be null.");
            if (specs.Length == 0)
                throw new ArgumentException("Stages array must not be empty.");
        }
        catch (Exception ex) when (ex is not ArgumentException) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Invalid stages JSON: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_entity_detail"]);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: ex.Message,
                SessionId: sessionId,
                Affordances: ["get_entity_detail"]);
        }

        return Evolve(sessionId, builder => {
            foreach (var s in specs)
                builder = s.ParentStageName is not null
                    ? builder.AddStage(entityName, s.Name, s.ParentStageName)
                    : builder.AddStage(entityName, s.Name);
            return builder;
        }, successAffordances: ["add_action", "add_action_to_stage", "get_entity_detail"]);
    }

    /// <summary>
    /// Places multiple actions onto stages in a single atomic batch.
    /// </summary>
    [McpServerTool(Name = "add_actions_to_stages"), Description("Places multiple actions onto stages in a single atomic batch. Provide a JSON array of {stageName, actionName} objects.")]
    public static DomainToolResponse AddActionsToStages(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity")] string entityName,
        [Description("JSON array of action-stage pairs: [{\"stageName\":\"Draft\",\"actionName\":\"Submit\"},...]")] string actions) {
        ActionToStageSpec[] specs;
        try {
            specs = JsonSerializer.Deserialize<ActionToStageSpec[]>(actions)
                ?? throw new ArgumentException("Actions array must not be null.");
            if (specs.Length == 0)
                throw new ArgumentException("Actions array must not be empty.");
        }
        catch (Exception ex) when (ex is not ArgumentException) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Invalid actions JSON: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_entity_detail"]);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: ex.Message,
                SessionId: sessionId,
                Affordances: ["get_entity_detail"]);
        }

        return Evolve(sessionId, builder => {
            foreach (var s in specs)
                builder = builder.AddActionToStage(entityName, s.StageName, s.ActionName);
            return builder;
        }, successAffordances: ["add_action", "get_entity_detail"]);
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
                var constraints = e.Properties.Sum(p => p.Constraints.Count);
                var stages = string.Join(",", e.Stages.OrderBy(s => s.Name)
                    .Select(s => $"{s.Name}({s.Actions.Count}a)"));
                var actions = $"A{e.Actions.Count}";
                var stageActions = e.Stages.Sum(s => s.Actions.Count);
                return $"{e.Name}:{props}({constraints}c)|[{stages}]|{actions}(+{stageActions}sa)";
            });
        return entityDetails.Any()
            ? $"{typeCounts}|{string.Join(",", entityDetails)}"
            : typeCounts;
    }

    private static DomainToolResponse Evolve(
        string sessionId,
        Func<EvolutionBuilder, EvolutionBuilder> mutate,
        IReadOnlyList<string>? successAffordances = null) {
        // Snapshot read for session-not-found check and fingerprint.
        // The actual mutation happens atomically inside McpSessionStore.Evolve.
        if (!McpSessionStore.TryGet(sessionId, out var snapshot))
            return new DomainToolResponse(
                Success: false,
                Message: $"Session '{sessionId}' not found.",
                Affordances: ["create_domain_session", "list_sessions"]);

        var before = GetFingerprint(snapshot.Domain);
        var preRevision = snapshot.Revision;

        var outcome = McpSessionStore.Evolve(sessionId, domain => {
            var result = new DomainEvolution(domain).Evolve();
            result = mutate(result);
            return result.Apply();
        });

        // Session was just validated above; null shouldn't happen but guard anyway.
        if (outcome is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Session '{sessionId}' not found.",
                Affordances: ["create_domain_session", "list_sessions"]);

        if (!outcome.Succeeded) {
            var diagnostics = outcome.FailureSummary is not null
                ? new List<string> { outcome.FailureSummary }
                : new List<string>();

            var errorMessages = outcome.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(3)
                .Select(d => d.Message)
                .ToList();
            diagnostics.AddRange(errorMessages);

            return new DomainToolResponse(
                Success: false,
                Message: $"Evolution rolled back: {outcome.FailureSummary ?? "Analysis failed"}",
                SessionId: sessionId,
                Revision: preRevision,
                Diagnostics: diagnostics.Count > 0 ? diagnostics : null,
                Affordances: ["get_domain_analysis", "get_domain_overview"]
            );
        }

        // Guard: detect silent no-ops
        var after = GetFingerprint(outcome.Root);
        if (before == after) {
            return new DomainToolResponse(
                Success: false,
                Message: "No changes applied: target entity not found or change had no effect. Check that the entity name is correct.",
                SessionId: sessionId,
                Revision: preRevision,
                Affordances: ["get_domain_overview", "get_entity_detail"]
            );
        }

        // McpSessionStore.Evolve already committed the update atomically.
        return new DomainToolResponse(
            Success: true,
            Message: "Change applied successfully.",
            SessionId: sessionId,
            Revision: preRevision + 1,
            Affordances: successAffordances
        );
    }

    // ── Batch tool spec types ───────────────────────────────────

    private sealed record PropertySpec(
        [property: System.Text.Json.Serialization.JsonPropertyName("name")] string Name,
        [property: System.Text.Json.Serialization.JsonPropertyName("typeName")] string TypeName);
    private sealed record StageSpec(
        [property: System.Text.Json.Serialization.JsonPropertyName("name")] string Name,
        [property: System.Text.Json.Serialization.JsonPropertyName("parentStageName")] string? ParentStageName = null);
    private sealed record ActionToStageSpec(
        [property: System.Text.Json.Serialization.JsonPropertyName("stageName")] string StageName,
        [property: System.Text.Json.Serialization.JsonPropertyName("actionName")] string ActionName);

    // ── Constraint tools ────────────────────────────────────────

    /// <summary>
    /// Adds a validation constraint to a property on an entity.
    /// </summary>
    [McpServerTool(Name = "add_constraint"), Description("Adds a validation constraint to a property. Supported types: Range (min/max), Required, Length (min/max), Pattern (regex), Unique.")]
    public static DomainToolResponse AddConstraint(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity")] string entityName,
        [Description("Name of the property")] string propertyName,
        [Description("Constraint type: Range, Required, Length, Pattern, Unique")] string constraintType,
        [Description("JSON config for the constraint, e.g. {\"min\":0,\"max\":100} for Range, {\"min\":5,\"max\":50} for Length, {\"regex\":\"^[a-z]+$\"} for Pattern")] string? config = null) {
        Constraint constraint;
        try {
            constraint = BuildConstraint(constraintType, config);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Invalid constraint: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_entity_detail"]);
        }

        return Evolve(sessionId, builder =>
                builder.AddConstraintToProperty(entityName, propertyName, constraint),
            successAffordances: ["get_entity_detail", "get_domain_analysis"]);
    }

    /// <summary>
    /// Lists all constraints on an entity's properties.
    /// </summary>
    [McpServerTool(Name = "get_constraints"), Description("Lists all constraints on an entity's properties. Optionally filter by property name.")]
    public static DomainToolResponse GetConstraints(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity")] string entityName,
        [Description("Optional property name filter")] string? propertyName = null) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return new DomainToolResponse(
                Success: false,
                Message: $"Session '{sessionId}' not found.",
                Affordances: ["create_domain_session", "list_sessions"]);

        var entity = state.Domain.Types.OfType<Entity>()
            .FirstOrDefault(e => string.Equals(e.Name, entityName, StringComparison.Ordinal));
        if (entity is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Entity '{entityName}' not found.",
                SessionId: sessionId,
                Affordances: ["get_domain_overview"]);

        var constraints = entity.Properties
            .Where(p => propertyName is null || string.Equals(p.Name, propertyName, StringComparison.Ordinal))
            .SelectMany(p => p.Constraints.Select(c => new {
                property = p.Name,
                type = c.GetType().Name.Replace("Constraint", ""),
                detail = FormatConstraint(c)
            }))
            .ToList();

        return new DomainToolResponse(
            Success: true,
            Message: $"{constraints.Count} constraint(s) on entity '{entityName}'.",
            SessionId: sessionId,
            Revision: state.Revision,
            Data: constraints,
            Affordances: ["add_constraint", "get_entity_detail"]
        );
    }

    private static Constraint BuildConstraint(string type, string? config) {
        var cfg = config is not null
            ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(config)
            : null;

        return type switch {
            "Range" => new RangeConstraint(
                cfg?.TryGetValue("min", out var min) == true ? NormalizeJsonElement(min) : null,
                cfg?.TryGetValue("max", out var max) == true ? NormalizeJsonElement(max) : null),
            "Required" => new RequiredConstraint(),
            "Length" => new LengthConstraint(
                cfg?.TryGetValue("min", out var lmin) == true ? lmin.GetInt32() : 0,
                cfg?.TryGetValue("max", out var lmax) == true ? lmax.GetInt32() : int.MaxValue),
            "Pattern" => new PatternConstraint(
                cfg?.TryGetValue("regex", out var rx) == true ? rx.GetString()! : throw new ArgumentException("Pattern requires 'regex' config.")),
            "Unique" => new UniqueConstraint(),
            _ => throw new ArgumentException($"Unknown constraint type '{type}'. Supported: Range, Required, Length, Pattern, Unique.")
        };
    }

    private static object? NormalizeJsonElement(JsonElement je) => je.ValueKind switch {
        JsonValueKind.Number when je.TryGetInt32(out var i) => i,
        JsonValueKind.Number when je.TryGetInt64(out var l) => l,
        JsonValueKind.Number => je.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => je.GetString(),
        JsonValueKind.Null => null,
        _ => je.GetRawText()
    };

    private static string FormatConstraint(Constraint c) => c switch {
        RangeConstraint r => $"Range(min={r.Minimum}, max={r.Maximum})",
        RequiredConstraint => "Required",
        LengthConstraint l => $"Length(min={l.MinLength}, max={l.MaxLength})",
        PatternConstraint p => $"Pattern({p.Pattern})",
        UniqueConstraint => "Unique",
        _ => c.GetType().Name
    };
}

/// <summary>
/// Tools for inspecting policy metadata (guard expressions) on domain entities.
/// Full VM-based policy evaluation with record-building from JSON args is deferred
/// to WP5 — see `docs/plans/v2-to-v3/workstreams/ws8-analysis-unification-and-lowering.md`.
/// </summary>
[McpServerToolType]
internal sealed class PolicyTool {
    /// <summary>
    /// Returns the guard expression text of a named policy on an entity.
    /// Use this to inspect what condition a policy enforces (e.g. "Age >= 18").
    /// </summary>
    [McpServerTool(Name = "get_policy_expression"), Description("Returns the guard expression text of a named policy on an entity for inspection.")]
    public static DomainToolResponse GetPolicyExpression(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity that has the policy")] string entityName,
        [Description("Name of the policy to inspect")] string policyName) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return new DomainToolResponse(
                Success: false,
                Message: $"Session '{sessionId}' not found.",
                Affordances: ["create_domain_session", "list_sessions"]);

        var entity = state.Domain.Types.OfType<Entity>()
            .FirstOrDefault(e => string.Equals(e.Name, entityName, StringComparison.Ordinal));
        if (entity is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Entity '{entityName}' not found.",
                SessionId: sessionId,
                Affordances: ["get_domain_overview", "add_entity"]);

        var policy = entity.Policies
            .FirstOrDefault(p => string.Equals(p.Name, policyName, StringComparison.Ordinal));
        if (policy is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Policy '{policyName}' not found on entity '{entityName}'.",
                SessionId: sessionId,
                Affordances: ["get_entity_detail"]);

        return new DomainToolResponse(
            Success: true,
            Message: $"Policy '{policyName}' on '{entityName}': {policy.Expression}",
            SessionId: sessionId,
            Revision: state.Revision,
            Data: new { policyName = policy.Name, entityName = entity.Name, expression = policy.Expression.ToString() },
            Affordances: ["get_entity_detail", "get_domain_overview"]
        );
    }

    /// <summary>
    /// Adds a policy with a guard expression to an entity. Accepts a single JSON
    /// expression string supporting comparisons, composites, and literals.
    /// </summary>
    [McpServerTool(Name = "add_policy"), Description("Adds a policy with a guard expression to an entity. Provide 'expression' as a JSON string: {\"property\":\"Age\",\"op\":\">=\",\"value\":18} for comparisons, {\"and\":[...]}/{\"or\":[...]}/{\"not\":{...}} for composites, or {\"literal\":true} for always-true guards.")]
    public static DomainToolResponse AddPolicy(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity")] string entityName,
        [Description("Policy name (e.g. 'Adult', 'LargeActive')")] string policyName,
        [Description("JSON expression string. Comparison: {\"property\":\"Age\",\"op\":\">=\",\"value\":18}. Composite: {\"and\":[{...},{...}]}, {\"or\":[...]}, {\"not\":{...}}. Literal: {\"literal\":true}.")] string expression) {
        // Parse the expression eagerly — this is pure and doesn't depend on session state.
        DomainExpression domainExpr;
        try {
            domainExpr = DomainExpressionJsonParser.ParseJson(expression);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Invalid policy expression: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_entity_detail", "get_domain_overview"]);
        }

        // Atomic read-modify-write through McpSessionStore.Evolve.
        var outcome = McpSessionStore.Evolve(sessionId, domain =>
            new DomainEvolution(domain).Evolve()
                .AddPolicyToEntity(entityName, policyName, domainExpr)
                .Apply());

        if (outcome is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Session '{sessionId}' not found.",
                Affordances: ["create_domain_session", "list_sessions"]);

        if (!outcome.Succeeded) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Evolution rolled back: {outcome.FailureSummary ?? "Analysis failed"}",
                SessionId: sessionId,
                Affordances: ["get_domain_analysis", "get_domain_overview"]);
        }

        // Need the session to get the revision for the response.
        // We know it exists because outcome is non-null.
        McpSessionStore.TryGet(sessionId, out var state);
        return new DomainToolResponse(
            Success: true,
            Message: $"Policy '{policyName}' added to entity '{entityName}'.",
            SessionId: sessionId,
            Revision: state?.Revision ?? 0,
            Affordances: ["get_entity_detail", "get_policy_expression", "evaluate_policy"]);
    }

    /// <summary>
    /// Evaluates a policy's guard expression against a sample subject.
    /// Provide <c>age</c> for simple Age-based policies, or <c>properties</c>
    /// as a JSON object for multi-property entities (e.g. {"Status":"Active","Total":200}).
    /// Returns the boolean result (true/false) from the VM.
    /// </summary>
    [McpServerTool(Name = "evaluate_policy"), Description("Evaluates a policy's guard expression against a sample subject. Provide 'age' for simple Age-based policies, or 'properties' as a JSON object for multi-property entities. Returns true if the policy passes, false otherwise.")]
    public static DomainToolResponse EvaluatePolicy(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity that has the policy")] string entityName,
        [Description("Name of the policy to evaluate")] string policyName,
        [Description("Age value for the sample subject (convenience for Age-based policies)")] int? age = null,
        [Description("JSON object of property values, e.g. \"{\\\"Status\\\":\\\"Active\\\",\\\"Total\\\":200}\"")] string? properties = null) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return new DomainToolResponse(
                Success: false,
                Message: $"Session '{sessionId}' not found.",
                Affordances: ["create_domain_session", "list_sessions"]);

        var entity = state.Domain.Types.OfType<Entity>()
            .FirstOrDefault(e => string.Equals(e.Name, entityName, StringComparison.Ordinal));
        if (entity is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Entity '{entityName}' not found.",
                SessionId: sessionId,
                Affordances: ["get_domain_overview", "add_entity"]);

        var policy = entity.Policies
            .FirstOrDefault(p => string.Equals(p.Name, policyName, StringComparison.Ordinal));
        if (policy is null)
            return new DomainToolResponse(
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
            return new DomainToolResponse(
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
            return new DomainToolResponse(
                Success: false,
                Message: $"Evaluation failed: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_policy_expression", "get_entity_detail"]);
        }

        var data = new { policyName, entityName, age, properties, result };

        return new DomainToolResponse(
            Success: true,
            Message: result ? "Policy passed (true)." : "Policy failed (false).",
            SessionId: sessionId,
            Revision: state.Revision,
            Data: data,
            Affordances: ["get_policy_expression", "get_entity_detail"]);
    }

    // ── Private helpers ─────────────────────────────────────────

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