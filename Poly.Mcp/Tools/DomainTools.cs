using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

using ModelContextProtocol.Server;

using Poly.Analysis;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Queries;
using Poly.Mcp.Sessions;

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
    [property: JsonPropertyName("navigations")] IReadOnlyList<NavigationData>? Navigations = null
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
internal sealed record NavigationData(
    [property: JsonPropertyName("relationshipName")] string RelationshipName,
    [property: JsonPropertyName("relatedEntity")] string RelatedEntityName,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("cardinality")] string Cardinality,
    [property: JsonPropertyName("sourceOwnsTarget")] bool SourceOwnsTarget
);

internal sealed record AnalysisData(
    [property: JsonPropertyName("errorCount")] int ErrorCount,
    [property: JsonPropertyName("warningCount")] int WarningCount,
    [property: JsonPropertyName("infoCount")] int InfoCount,
    [property: JsonPropertyName("hintCount")] int HintCount,
    [property: JsonPropertyName("hasStructuralFailure")] bool HasStructuralFailure,
    [property: JsonPropertyName("messages")] IReadOnlyList<string> Messages,
    [property: JsonPropertyName("entityCount")] int EntityCount = 0,
    [property: JsonPropertyName("relationshipCount")] int RelationshipCount = 0,
    [property: JsonPropertyName("rootEntityNames")] IReadOnlyList<string>? RootEntityNames = null,
    [property: JsonPropertyName("createInCount")] int CreateInCount = 0,
    [property: JsonPropertyName("subscriptionCount")] int SubscriptionCount = 0,
    [property: JsonPropertyName("actionSummary")] IReadOnlyList<ActionFact>? ActionSummary = null,
    [property: JsonPropertyName("hasStorageMapping")] bool HasStorageMapping = false,
    [property: JsonPropertyName("aggregateRootCount")] int AggregateRootCount = 0,
    [property: JsonPropertyName("aggregates")] IReadOnlyList<AggregateFact>? Aggregates = null,
    [property: JsonPropertyName("subscriptionPlans")] IReadOnlyList<SubscriptionPlanFact>? SubscriptionPlans = null
);

internal sealed record ActionFact(
    [property: JsonPropertyName("entityName")] string EntityName,
    [property: JsonPropertyName("actionName")] string ActionName,
    [property: JsonPropertyName("stageName")] string? StageName,
    [property: JsonPropertyName("resultType")] string? ResultTypeName
);

/// <summary>Aggregate ownership fact: one root plus its transitive members.</summary>
internal sealed record AggregateFact(
    [property: JsonPropertyName("rootName")] string RootName,
    [property: JsonPropertyName("memberNames")] IReadOnlyList<string> MemberNames
);

/// <summary>
/// Stage (or entity-level, when <see cref="StageName"/> is null) subscription plan
/// for a relationship — which target stages it watches and with which quantifiers.
/// </summary>
internal sealed record SubscriptionPlanFact(
    [property: JsonPropertyName("entityName")] string EntityName,
    [property: JsonPropertyName("stageName")] string? StageName,
    [property: JsonPropertyName("relationshipName")] string RelationshipName,
    [property: JsonPropertyName("targetStageNames")] IReadOnlyList<string> TargetStageNames,
    [property: JsonPropertyName("quantifiers")] IReadOnlyList<string> Quantifiers
);

// ── Tool classes ───────────────────────────────────────────────

/// <summary>
/// Tools for managing V3 domain sessions.
/// </summary>
[McpServerToolType]
internal sealed class SessionTool {
    /// <summary>
    /// Creates a new domain session with the canonical built-in primitive types.
    /// The bootstrapped domain includes canonical primitives (Boolean, Number, Text, Uuid, Binary)
    /// plus Temporal types (Date, Time, DateTime, Duration) from product <c>uses temporal</c>.
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
            Affordances: ["add", "get_domain_overview"]
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
                ? ["add"]
                : ["get_entity_detail", "add"]
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

        var detail = DomainQueries.GetEntity(state.Domain, entityName, state.LatestAnalysis);
        if (detail is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Entity '{entityName}' not found.",
                SessionId: sessionId,
                Affordances: ["get_domain_overview", "add"]
            );

        var data = new EntityDetailData(
            detail.Name,
            detail.Properties.Select(p => new PropertyData(p.Name, p.TypeName, p.ConstraintCount)).ToList(),
            detail.Stages.Select(s => new StageData(
                s.Name, s.ActionNames,
                s.Subscriptions.Select(sub => new SubscriptionData(
                    sub.RelationshipName, sub.StageNames, sub.Quantifier, sub.EffectCount)).ToList())).ToList(),
            detail.Actions.Select(a => new ActionData(a.Name, a.ParameterNames, a.EffectCount)).ToList(),
            detail.Policies.Select(p => p.Name).ToList(),
            detail.Navigations.Select(n => new NavigationData(
                n.RelationshipName, n.RelatedEntityName, n.Role, n.Cardinality, n.SourceOwnsTarget)).ToList()
        );

        return new DomainToolResponse(
            Success: true,
            Message: $"Entity '{entityName}': {detail.Properties.Count} properties, {detail.Stages.Count} stages, {detail.Actions.Count} actions.",
            SessionId: sessionId,
            Revision: state.Revision,
            Data: data,
            Affordances: ["add", "get_domain_overview"]
        );
    }

    /// <summary>
    /// Returns analysis diagnostics and structured domain facts for the current domain state.
    /// Structured facts (roots, aggregates, topology, action names, subscription plans) are
    /// derived from LatestAnalysis metadata.
    /// </summary>
    [McpServerTool(Name = "get_domain_analysis"), Description("Returns analysis diagnostics and structured domain facts (entity structure, aggregates, topology, actions, subscription plans) for the current domain state.")]
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

        // SA′.3: Count hint diagnostics separately from infoCount (Hint ≠ Information severity).
        var hintCount = state.LatestAnalysis.Diagnostics
            .Count(d => d.Severity == DiagnosticSeverity.Hint);

        // ── Structured facts from LatestAnalysis metadata ──

        var entityCount = state.Domain.Types.OfType<Entity>().Count();
        var relationshipCount = state.LatestAnalysis.GetAllRelationships(state.Domain).Count;

        // Root entities from EntityStructureMetadata only (aggregate copies that bit).
        var rootEntityNames = state.Domain.Types.OfType<Entity>()
            .Where(e => state.LatestAnalysis.GetStructure(e)?.IsRoot == true)
            .Select(e => e.Name)
            .ToList();

        // Topology summary from EffectTopologyMetadata
        var topology = state.LatestAnalysis.GetMetadata<EffectTopologyMetadata>(state.Domain)?.Topology;
        var createInCount = topology?.CreateInRelations.Count ?? 0;
        var subscriptionCount = topology?.Subscriptions.Count ?? 0;

        // Action names projected from capability facts
        var behavior = BehaviorMetadata.From(state.Domain, state.LatestAnalysis);
        var actionSummary = behavior?.Entities
            .SelectMany(e => e.Actions.Select(a => new ActionFact(
                EntityName: e.Name,
                ActionName: a.Name,
                StageName: a.StageName,
                ResultTypeName: a.ResultTypeName)))
            .ToList();

        // Infrastructure boolean
        var hasStorage = state.LatestAnalysis.GetMetadata<StorageMappingMetadata>(state.Domain) is not null;

        // Aggregate ownership from OwnershipAggregateMetadata (OwnershipAggregatePass)
        var aggregate = state.LatestAnalysis.GetMetadata<OwnershipAggregateMetadata>(state.Domain)?.Aggregate;
        var aggregates = new List<AggregateFact>();
        if (aggregate is not null) {
            var aggregateByName = aggregate.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
            foreach (var root in aggregate.Entities.Where(e => e.IsRoot).OrderBy(e => e.Name)) {
                // Transitive members: entities whose parent chain leads to this root.
                var members = new List<string>();
                foreach (var candidate in aggregate.Entities.Where(e => !e.IsRoot)) {
                    var cursor = candidate;
                    while (cursor?.AggregateParentName is not null) {
                        if (string.Equals(cursor.AggregateParentName, root.Name, StringComparison.Ordinal)) {
                            members.Add(candidate.Name);
                            break;
                        }
                        cursor = aggregateByName.GetValueOrDefault(cursor.AggregateParentName);
                    }
                }
                members.Sort(StringComparer.Ordinal);
                aggregates.Add(new AggregateFact(root.Name, members));
            }
        }

        // Stages (and entity-level subscriptions) with non-empty dispatch plans —
        // SubscriptionDispatchPlanMetadata published by RuntimeContractAnalyzer.
        var subscriptionPlans = new List<SubscriptionPlanFact>();
        foreach (var entity in state.Domain.Types.OfType<Entity>()) {
            var entityPlan = state.LatestAnalysis.GetMetadata<SubscriptionDispatchPlanMetadata>(entity);
            if (entityPlan is not null) {
                foreach (var (relName, entries) in entityPlan.ByRelationshipName) {
                    subscriptionPlans.Add(new SubscriptionPlanFact(
                        entity.Name, null, relName,
                        entries.SelectMany(e => e.StageNames).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList(),
                        entries.Select(e => e.Quantifier.ToString()).Distinct().OrderBy(q => q, StringComparer.Ordinal).ToList()));
                }
            }
            foreach (var stage in entity.Stages) {
                var stagePlan = state.LatestAnalysis.GetMetadata<SubscriptionDispatchPlanMetadata>(stage);
                if (stagePlan is not null) {
                    foreach (var (relName, entries) in stagePlan.ByRelationshipName) {
                        subscriptionPlans.Add(new SubscriptionPlanFact(
                            entity.Name, stage.Name, relName,
                            entries.SelectMany(e => e.StageNames).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList(),
                            entries.Select(e => e.Quantifier.ToString()).Distinct().OrderBy(q => q, StringComparer.Ordinal).ToList()));
                    }
                }
            }
        }

        var data = new AnalysisData(
            summary.ErrorCount, summary.WarningCount, summary.InfoCount, hintCount,
            summary.HasStructuralFailure, summary.Messages,
            EntityCount: entityCount,
            RelationshipCount: relationshipCount,
            RootEntityNames: rootEntityNames.Count > 0 ? rootEntityNames : null,
            CreateInCount: createInCount,
            SubscriptionCount: subscriptionCount,
            ActionSummary: actionSummary?.Count > 0 ? actionSummary : null,
            HasStorageMapping: hasStorage,
            AggregateRootCount: aggregates.Count,
            Aggregates: aggregates.Count > 0 ? aggregates : null,
            SubscriptionPlans: subscriptionPlans.Count > 0 ? subscriptionPlans : null
        );

        var message = summary.ErrorCount > 0
            ? $"{summary.ErrorCount} error(s), {summary.WarningCount} warning(s). See diagnostics for details."
            : $"{summary.InfoCount} info(s), {summary.WarningCount} warning(s). {hintCount} hint(s). No errors.";

        // Build affordances — include get_domain_suggestions when hints exist.
        var affordances = new List<string> { "get_domain_overview" };
        if (hintCount > 0)
            affordances.Add("get_domain_suggestions");

        return new DomainToolResponse(
            Success: true,
            Message: message,
            SessionId: sessionId,
            Revision: state.Revision,
            Data: data,
            Diagnostics: summary.Messages.Count > 0 ? summary.Messages : null,
            Affordances: affordances
        );
    }

    /// <summary>
    /// Returns authoring suggestions (advisory hints) for the current domain.
    /// Suggestions identify common gaps like missing stages, actions, or policies.
    /// </summary>
    [McpServerTool(Name = "get_domain_suggestions"), Description("Returns authoring suggestions (advisory hints) for the current domain. Suggestions identify common gaps like missing stages, actions, or policies — they are advisory and do not block evolution.")]
    public static DomainToolResponse GetDomainSuggestions(
        [Description("Session ID")] string sessionId) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        if (state.LatestAnalysis is null)
            return new DomainToolResponse(
                Success: false,
                Message: "No analysis available.",
                SessionId: sessionId,
                Affordances: ["get_domain_overview", "get_domain_analysis"]);

        var hints = state.LatestAnalysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Hint &&
                        string.Equals(d.Code, DomainModelDiagnosticCodes.AuthoringSuggestion, StringComparison.Ordinal))
            .Select(d => new {
                message = d.Message,
                code = d.Code,
                nodeId = d.Node.Id.ToString()
            })
            .ToList();

        var message = hints.Count > 0
            ? $"{hints.Count} suggestion(s) available."
            : "No suggestions at this time. Domain has no obvious gaps requiring authoring suggestions.";

        return new DomainToolResponse(
            Success: true,
            Message: message,
            SessionId: sessionId,
            Revision: state.Revision,
            Data: new { suggestions = hints, count = hints.Count },
            Affordances: ["get_entity_detail", "get_domain_analysis", "add"]
        );
    }

    /// <summary>
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
            Affordances: ["add", "get_entity_detail"]
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

    // ── Unified add (kind + payload) ────────────────────────────

    /// <summary>
    /// Creates one domain definition element, dispatched by <c>kind</c> + <c>payload</c>.
    /// Incremental structure edits only — bulk structure, effects, and subscriptions go
    /// through <c>apply_dsl</c>. Expression bodies are product DSL text, never JSON IR.
    /// </summary>
    [McpServerTool(Name = "add"), Description(@"Creates one domain definition element. kind is case-sensitive: entity, property, stage, action, stage_action, relationship, constraint, policy, value_type, contract, contract_value_type, contract_endpoint, contract_binding. payload is a JSON object of kind-specific fields:
- entity: {""name"":""Order""}
- property: {""entityName"":""Order"",""name"":""Total"",""typeName"":""Number""}
- stage: {""entityName"":""Order"",""name"":""Active""}
- action: {""entityName"":""Order"",""name"":""Submit""}
- stage_action: {""entityName"":""Order"",""stageName"":""Draft"",""name"":""Submit""}
- relationship: {""name"":""OrderLines"",""source"":""Order"",""target"":""Line"",""cardinality"":""OneToMany""} (cardinality: OneToOne, OneToMany, ManyToMany, ManyToOne; source/target may also be sourceEntityName/targetEntityName)
- constraint: {""entityName"":""Order"",""propertyName"":""Total"",""type"":""Range"",""min"":0,""max"":100} (types: Required, Unique, Range, Length, Pattern; Pattern needs {""pattern"":""^[a-z]+$""})
- policy: {""entityName"":""Order"",""name"":""Adult"",""expression"":""Age >= 18""} — expression is DSL text only, never JSON
- value_type: {""name"":""Money""}
- contract: {""name"":""Stripe"",""sourceKind"":""ExternalProvider"",""source"":""stripe"",""version"":""v1""}
- contract_value_type: {""contractName"":""Stripe"",""name"":""ChargeRequest""}
- contract_endpoint: {""contractName"":""Stripe"",""name"":""Charge"",""kind"":""Operation"",""direction"":""Inbound"",""payloadType"":""Number""}
- contract_binding: {""name"":""ChargeOrder"",""contractName"":""Stripe"",""endpointName"":""Charge"",""actionName"":""Pay"",""parameter"":""amount""}
Unknown kind, missing required field, or invalid cardinality fails closed. For bulk structure, effects, or subscriptions use apply_dsl.")]
    public static DomainToolResponse Add(
        [Description("Session ID returned by create_domain_session")] string sessionId,
        [Description("Domain element kind (case-sensitive): entity, property, stage, action, stage_action, relationship, constraint, policy")] string kind,
        [Description("JSON object of kind-specific fields (see tool description for per-kind payloads)")] string payload) {
        if (!McpSessionStore.TryGet(sessionId, out _))
            return new DomainToolResponse(
                Success: false,
                Message: $"Session '{sessionId}' not found.",
                Affordances: ["create_domain_session", "list_sessions"]);

        JsonElement root;
        try {
            using var doc = JsonDocument.Parse(payload);
            root = doc.RootElement.Clone();
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Invalid payload JSON: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_domain_overview", "apply_dsl"]);
        }

        switch (kind) {
            case "entity": {
                    var name = Field(root, "name");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    return Evolve(sessionId, builder => builder.AddEntity(name),
                        successAffordances: ["add", "apply_dsl", "get_entity_detail"]);
                }
            case "property": {
                    var entityName = Field(root, "entityName");
                    var name = Field(root, "name");
                    var typeName = Field(root, "typeName");
                    if (entityName is null) return MissingField(sessionId, kind, "entityName");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    if (typeName is null) return MissingField(sessionId, kind, "typeName");
                    return Evolve(sessionId, builder =>
                            builder.AddPropertyToEntity(entityName, new Property(name, new DomainTypeReference(typeName), [])),
                        successAffordances: ["add", "apply_dsl", "get_entity_detail"]);
                }
            case "stage": {
                    var entityName = Field(root, "entityName");
                    var name = Field(root, "name");
                    if (entityName is null) return MissingField(sessionId, kind, "entityName");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    return Evolve(sessionId, builder => builder.AddStage(entityName, name),
                        successAffordances: ["add", "apply_dsl", "get_entity_detail"]);
                }
            case "action": {
                    var entityName = Field(root, "entityName");
                    var name = Field(root, "name");
                    if (entityName is null) return MissingField(sessionId, kind, "entityName");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    return Evolve(sessionId, builder => builder.AddAction(entityName, name),
                        successAffordances: ["add", "apply_dsl", "get_entity_detail"]);
                }
            case "stage_action": {
                    var entityName = Field(root, "entityName");
                    var stageName = Field(root, "stageName");
                    var name = Field(root, "name");
                    if (entityName is null) return MissingField(sessionId, kind, "entityName");
                    if (stageName is null) return MissingField(sessionId, kind, "stageName");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    return Evolve(sessionId, builder => builder.AddActionToStage(entityName, stageName, name),
                        successAffordances: ["add", "apply_dsl", "get_entity_detail"]);
                }
            case "relationship": {
                    var name = Field(root, "name");
                    var source = Field(root, "source", "sourceEntityName");
                    var target = Field(root, "target", "targetEntityName");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    if (source is null) return MissingField(sessionId, kind, "source");
                    if (target is null) return MissingField(sessionId, kind, "target");
                    var cardText = Field(root, "cardinality");
                    if (!TryParseCardinality(cardText, out var card))
                        return UnknownCardinality(sessionId, cardText ?? "");
                    var owns = root.TryGetProperty("sourceOwnsTarget", out var ownProp)
                        && ownProp.ValueKind == JsonValueKind.True;
                    return Evolve(sessionId, builder =>
                            builder.AddRelationship(name, source, target, card, owns),
                        successAffordances: ["add", "apply_dsl", "get_entity_detail"]);
                }
            case "constraint": {
                    var entityName = Field(root, "entityName");
                    var propertyName = Field(root, "propertyName");
                    var type = Field(root, "type");
                    if (entityName is null) return MissingField(sessionId, kind, "entityName");
                    if (propertyName is null) return MissingField(sessionId, kind, "propertyName");
                    if (type is null) return MissingField(sessionId, kind, "type");
                    return AddConstraintCore(sessionId, entityName, propertyName, type, payload);
                }
            case "policy": {
                    var entityName = Field(root, "entityName");
                    var name = Field(root, "name");
                    var expression = Field(root, "expression");
                    if (entityName is null) return MissingField(sessionId, kind, "entityName");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    if (expression is null) return MissingField(sessionId, kind, "expression");
                    return AddPolicyCore(sessionId, entityName, name, expression);
                }
            case "value_type": {
                    var name = Field(root, "name");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    return Evolve(sessionId, builder => builder.AddValueType(name),
                        successAffordances: ["add", "apply_dsl", "get_domain_overview"]);
                }
            case "contract": {
                    var name = Field(root, "name");
                    var source = Field(root, "source", "sourceIdentifier");
                    var version = Field(root, "version");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    if (source is null) return MissingField(sessionId, kind, "source");
                    if (version is null) return MissingField(sessionId, kind, "version");
                    var sourceKindText = Field(root, "sourceKind") ?? "ExternalProvider";
                    if (!Enum.TryParse<ContractSourceKind>(sourceKindText, ignoreCase: true, out var sourceKind))
                        return new DomainToolResponse(Success: false,
                            Message: $"Unknown sourceKind '{sourceKindText}'. Use ExternalProvider or InternalDomain.",
                            SessionId: sessionId, Affordances: ["apply_dsl"]);
                    return Evolve(sessionId, builder => builder.AddImportedContract(name, sourceKind, source, version),
                        successAffordances: ["add", "apply_dsl", "get_domain_overview"]);
                }
            case "contract_value_type": {
                    var contractName = Field(root, "contractName");
                    var name = Field(root, "name");
                    if (contractName is null) return MissingField(sessionId, kind, "contractName");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    return Evolve(sessionId, builder => builder.AddContractValueType(
                            contractName, new Poly.DomainModeling.Ontology.ValueType(name, [], [])),
                        successAffordances: ["add", "apply_dsl"]);
                }
            case "contract_endpoint": {
                    var contractName = Field(root, "contractName");
                    var name = Field(root, "name");
                    var payloadType = Field(root, "payloadType");
                    if (contractName is null) return MissingField(sessionId, kind, "contractName");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    if (payloadType is null) return MissingField(sessionId, kind, "payloadType");
                    var kindText = Field(root, "kind") ?? "Operation";
                    var dirText = Field(root, "direction") ?? "Inbound";
                    if (!Enum.TryParse<ContractEndpointKind>(kindText, ignoreCase: true, out var epKind))
                        return new DomainToolResponse(Success: false,
                            Message: $"Unknown endpoint kind '{kindText}'. Use Operation or Event.",
                            SessionId: sessionId, Affordances: ["apply_dsl"]);
                    if (!Enum.TryParse<ContractEndpointDirection>(dirText, ignoreCase: true, out var dir))
                        return new DomainToolResponse(Success: false,
                            Message: $"Unknown direction '{dirText}'. Use Inbound or Outbound.",
                            SessionId: sessionId, Affordances: ["apply_dsl"]);
                    return Evolve(sessionId, builder => builder.AddContractEndpoint(contractName,
                            new ContractEndpoint(name, epKind, dir, new DomainTypeReference(payloadType))),
                        successAffordances: ["add", "apply_dsl"]);
                }
            case "contract_binding": {
                    var name = Field(root, "name");
                    var contractName = Field(root, "contractName");
                    var endpointName = Field(root, "endpointName");
                    var actionName = Field(root, "actionName");
                    var parameter = Field(root, "parameter", "localParameterName");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    if (contractName is null) return MissingField(sessionId, kind, "contractName");
                    if (endpointName is null) return MissingField(sessionId, kind, "endpointName");
                    if (actionName is null) return MissingField(sessionId, kind, "actionName");
                    if (parameter is null) return MissingField(sessionId, kind, "parameter");
                    return Evolve(sessionId, builder => builder.AddContractBinding(
                            name, contractName, endpointName, actionName, parameter),
                        successAffordances: ["add", "apply_dsl", "get_domain_analysis"]);
                }
            default:
                return new DomainToolResponse(
                    Success: false,
                    Message: $"Unknown kind '{kind}'. Allowed kinds: entity, property, stage, action, stage_action, relationship, constraint, policy, value_type, contract, contract_value_type, contract_endpoint, contract_binding. Bulk structure/effects → apply_dsl.",
                    SessionId: sessionId,
                    Affordances: ["apply_dsl", "get_domain_overview"]);
        }
    }

    private static DomainToolResponse AddConstraintCore(
        string sessionId, string entityName, string propertyName, string type, string payloadJson) {
        Constraint constraint;
        try {
            constraint = BuildConstraint(type, payloadJson);
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
            successAffordances: ["add", "apply_dsl", "get_entity_detail", "get_domain_analysis"]);
    }

    internal static DomainToolResponse AddPolicyCore(
        string sessionId, string entityName, string policyName, string expressionDsl) {
        // Session-first (consistent with other tools); parse with the session's
        // Same session tables as apply_dsl so concept folds (Now, durations) agree.
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return new DomainToolResponse(
                Success: false,
                Message: $"Session '{sessionId}' not found.",
                Affordances: ["create_domain_session", "list_sessions"]);

        DomainExpression expr;
        try {
            expr = DslExpressionFragment.ParseExpressionFragment(expressionDsl, state.Modeling);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Invalid policy expression: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_entity_detail", "get_domain_overview"]);
        }
        var result = Evolve(sessionId, builder => builder.AddPolicyToEntity(entityName, policyName, expr),
            successAffordances: ["add", "apply_dsl", "get_entity_detail", "get_policy_expression", "evaluate_policy"]);
        return result.Success
            ? result with { Message = $"Policy '{policyName}' added to entity '{entityName}'." }
            : result;
    }

    private static DomainToolResponse UnknownCardinality(string sessionId, string cardinality) =>
        new(Success: false,
            Message: $"Unknown cardinality '{cardinality}'. Allowed: OneToOne, OneToMany, ManyToMany, ManyToOne.",
            SessionId: sessionId,
            Affordances: ["get_domain_overview", "add"]);

    /// <summary>Maps a payload cardinality string to the enum. Null (omitted) defaults to OneToMany, matching the documented contract.</summary>
    private static bool TryParseCardinality(string? text, out RelationshipCardinality card) {
        switch (text) {
            case null:
                card = RelationshipCardinality.OneToMany;
                return true;
            case "OneToOne":
                card = RelationshipCardinality.OneToOne;
                return true;
            case "OneToMany":
                card = RelationshipCardinality.OneToMany;
                return true;
            case "ManyToMany":
                card = RelationshipCardinality.ManyToMany;
                return true;
            case "ManyToOne":
                card = RelationshipCardinality.ManyToOne;
                return true;
            default:
                card = default;
                return false;
        }
    }

    private static string? Field(JsonElement payload, params string[] names) {
        foreach (var name in names) {
            if (payload.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String) {
                var value = prop.GetString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        return null;
    }

    private static DomainToolResponse MissingField(string sessionId, string kind, string field) =>
        new(Success: false,
            Message: $"Kind '{kind}' requires payload field '{field}'.",
            SessionId: sessionId,
            Affordances: ["apply_dsl", "get_domain_overview"]);

    // ── Unified remove (kind + payload) ─────────────────────────

    /// <summary>
    /// Removes one domain definition element by identity, dispatched by
    /// <c>kind</c> + <c>payload</c>. Identity fields only — no expression bodies.
    /// </summary>
    [McpServerTool(Name = "remove"), Description(@"Removes one domain definition element by identity. kind is case-sensitive: entity, property, stage, action, stage_action, relationship, policy. payload is a JSON object of identity fields:
- entity: {""name"":""Order""}
- property: {""entityName"":""Order"",""name"":""Total""}
- stage: {""entityName"":""Order"",""name"":""Active""}
- action: {""entityName"":""Order"",""name"":""Submit""}
- stage_action: {""entityName"":""Order"",""stageName"":""Draft"",""name"":""Submit""}
- relationship: {""name"":""OrderLines""} — optional ""source"": {""name"":""OrderLines"",""source"":""Order""} to disambiguate when the same name is declared on multiple source entities
- policy: {""entityName"":""Order"",""name"":""Adult""} — optional scope: add ""stageName"" to remove a stage-scoped policy, ""actionName"" for an action-scoped policy (provide at most one)
- constraint: not implemented in unified remove (core constraint removal is instance-identity-based, unusable from payload identity) — author via add(kind: constraint) or apply_dsl
Unknown kind or missing required field fails closed.")]
    public static DomainToolResponse Remove(
        [Description("Session ID returned by create_domain_session")] string sessionId,
        [Description("Domain element kind (case-sensitive): entity, property, stage, action, stage_action, relationship, policy")] string kind,
        [Description("JSON object of identity fields (see tool description)")] string payload) {
        if (!McpSessionStore.TryGet(sessionId, out _))
            return new DomainToolResponse(
                Success: false,
                Message: $"Session '{sessionId}' not found.",
                Affordances: ["create_domain_session", "list_sessions"]);

        JsonElement root;
        try {
            using var doc = JsonDocument.Parse(payload);
            root = doc.RootElement.Clone();
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Invalid payload JSON: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_domain_overview", "apply_dsl"]);
        }

        switch (kind) {
            case "entity": {
                    var name = Field(root, "name");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    return Evolve(sessionId, builder => builder.RemoveEntity(name),
                        successAffordances: ["remove", "add", "apply_dsl", "get_domain_overview"]);
                }
            case "property": {
                    var entityName = Field(root, "entityName");
                    var name = Field(root, "name");
                    if (entityName is null) return MissingField(sessionId, kind, "entityName");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    return Evolve(sessionId, builder => builder.RemovePropertyFromEntity(entityName, name),
                        successAffordances: ["remove", "add", "apply_dsl", "get_entity_detail"]);
                }
            case "stage": {
                    var entityName = Field(root, "entityName");
                    var name = Field(root, "name");
                    if (entityName is null) return MissingField(sessionId, kind, "entityName");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    return Evolve(sessionId, builder => builder.RemoveStage(entityName, name),
                        successAffordances: ["remove", "add", "apply_dsl", "get_entity_detail"]);
                }
            case "action": {
                    var entityName = Field(root, "entityName");
                    var name = Field(root, "name");
                    if (entityName is null) return MissingField(sessionId, kind, "entityName");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    return Evolve(sessionId, builder => builder.RemoveAction(entityName, name),
                        successAffordances: ["remove", "add", "apply_dsl", "get_entity_detail"]);
                }
            case "stage_action": {
                    var entityName = Field(root, "entityName");
                    var stageName = Field(root, "stageName");
                    var name = Field(root, "name");
                    if (entityName is null) return MissingField(sessionId, kind, "entityName");
                    if (stageName is null) return MissingField(sessionId, kind, "stageName");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    return Evolve(sessionId, builder => builder.RemoveActionFromStage(entityName, stageName, name),
                        successAffordances: ["remove", "add", "apply_dsl", "get_entity_detail"]);
                }
            case "relationship": {
                    var name = Field(root, "name");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    // Relationship identity is (source entity, name). Optional 'source'
                    // disambiguates when the name is declared on multiple entities.
                    var source = Field(root, "source", "sourceEntityName");
                    if (source is null && McpSessionStore.TryGet(sessionId, out var state) && state.Domain is not null) {
                        var sources = (state.LatestAnalysis?.GetAllRelationships(state.Domain) ?? [])
                            .Where(r => string.Equals(r.Name, name, StringComparison.Ordinal))
                            .Select(r => r.Source.TypeName)
                            .Distinct(StringComparer.Ordinal)
                            .ToList();
                        if (sources.Count > 1)
                            return new DomainToolResponse(Success: false,
                                Message: $"Relationship '{name}' exists on multiple source entities ({string.Join(", ", sources)}). Provide 'source' to disambiguate.",
                                SessionId: sessionId, Affordances: ["remove", "get_relationships", "get_domain_analysis"]);
                        if (sources.Count == 1)
                            source = sources[0];
                    }
                    if (source is null)
                        return new DomainToolResponse(Success: false,
                            Message: $"Relationship '{name}' not found — nothing to remove.",
                            SessionId: sessionId, Affordances: ["remove", "get_domain_overview"]);
                    return Evolve(sessionId, builder => builder.RemoveRelationship(source, name),
                        successAffordances: ["remove", "add", "apply_dsl", "get_domain_overview"]);
                }
            case "policy": {
                    var entityName = Field(root, "entityName");
                    var name = Field(root, "name");
                    var stageName = Field(root, "stageName");
                    var actionName = Field(root, "actionName");
                    if (entityName is null) return MissingField(sessionId, kind, "entityName");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    if (stageName is not null && actionName is not null)
                        return new DomainToolResponse(
                            Success: false,
                            Message: "Provide at most one of 'stageName' or 'actionName' for policy scope.",
                            SessionId: sessionId,
                            Affordances: ["get_entity_detail", "get_domain_analysis"]);
                    return Evolve(sessionId, builder =>
                            stageName is not null
                                ? builder.RemovePolicyFromStage(entityName, stageName, name)
                                : actionName is not null
                                    ? builder.RemovePolicyFromAction(entityName, actionName, name)
                                    : builder.RemovePolicyFromEntity(entityName, name),
                        successAffordances: ["remove", "add", "apply_dsl", "get_entity_detail"]);
                }
            case "constraint":
                return new DomainToolResponse(
                    Success: false,
                    Message: "constraint remove not implemented in unified remove — core constraint removal is instance-identity-based (RemoveConstraintFromPropertyChange removes by ReferenceEquals) so a payload-identity tool cannot target it; author constraints via add(kind: constraint) or apply_dsl.",
                    SessionId: sessionId,
                    Affordances: ["add", "apply_dsl", "get_entity_detail"]);
            default:
                return new DomainToolResponse(
                    Success: false,
                    Message: $"Unknown kind '{kind}'. Allowed kinds: entity, property, stage, action, stage_action, relationship, policy. Bulk structure/effects → apply_dsl.",
                    SessionId: sessionId,
                    Affordances: ["apply_dsl", "get_domain_overview"]);
        }
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
        var typeCounts = $"T:{domain.Types.Count}|R:{domain.Types.OfType<Entity>().SelectMany(e => e.Navigations).Count()}";
        var entityDetails = domain.Types
            .OfType<Entity>()
            .OrderBy(e => e.Name)
            .Select(e => {
                var props = $"P{e.Properties.Count}";
                var constraints = e.Properties.Sum(p => p.Constraints.Count);
                var stages = string.Join(",", e.Stages.OrderBy(s => s.Name)
                    .Select(s => $"{s.Name}({s.Actions.Count}a,{s.Policies.Count}p)"));
                var actions = $"A{e.Actions.Count}({e.Actions.Sum(a => a.Policies.Count)}ap)";
                var stageActions = e.Stages.Sum(s => s.Actions.Count);
                var entityPolicies = e.Policies.Count;
                return $"{e.Name}:{props}({constraints}c)E{entityPolicies}|[{stages}]|{actions}(+{stageActions}sa)";
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

        var outcome = McpSessionStore.Evolve(sessionId, (domain, modeling) => {
            var result = new DomainEvolution(domain).Evolve();
            result = mutate(result);
            return result.Apply(session: modeling);
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

    // ── Constraint tools ────────────────────────────────────────

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
            Affordances: ["add", "get_entity_detail"]
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
                cfg is not null && (cfg.TryGetValue("pattern", out var p) || cfg.TryGetValue("regex", out p))
                    ? p.GetString()!
                    : throw new ArgumentException("Pattern requires 'pattern' config.")),
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
/// Tools for inspecting and evaluating policy guards on domain entities.
/// <c>get_policy_expression</c> is inspect-only; <c>evaluate_policy</c> runs the
/// VM path against a store instance from <c>create_instance</c>.
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
                Affordances: ["get_domain_overview", "add"]);

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
    /// Evaluates a named policy on a store instance from <c>create_instance</c>.
    /// </summary>
    [McpServerTool(Name = "evaluate_policy"), Description("Evaluates a named policy on a store instance. Create the subject with create_instance (and link_instances for cross-entity reads). instanceId is required — there is no bag/age/properties mode. Runs the lowered expression through Interpreter with the session Store bound. Returns true if the policy passes, false otherwise.")]
    public static DomainToolResponse EvaluatePolicy(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity that has the policy")] string entityName,
        [Description("Name of the policy to evaluate")] string policyName,
        [Description("Instance ID from create_instance. Required.")] string instanceId) {
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
                Affordances: ["get_domain_overview", "add"]);

        var policy = entity.Policies
            .FirstOrDefault(p => string.Equals(p.Name, policyName, StringComparison.Ordinal));
        if (policy is null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Policy '{policyName}' not found on entity '{entityName}'.",
                SessionId: sessionId,
                Affordances: ["get_entity_detail"]);

        if (string.IsNullOrWhiteSpace(instanceId))
            return new DomainToolResponse(
                Success: false,
                Message: "instanceId is required. Create the subject with create_instance, then pass that id.",
                SessionId: sessionId,
                Affordances: ["create_instance", "list_instances"]);

        bool result;
        try {
            if (!state.InstanceMap.TryGetValue(instanceId, out var existingInstance))
                return new DomainToolResponse(
                    Success: false,
                    Message: $"Instance '{instanceId}' not found in session. Create it first via create_instance.",
                    SessionId: sessionId,
                    Affordances: ["create_instance", "list_instances"]);

            if (!string.Equals(existingInstance.Entity.Name, entityName, StringComparison.Ordinal))
                return new DomainToolResponse(
                    Success: false,
                    Message: $"Instance '{instanceId}' is for entity '{existingInstance.Entity.Name}', not '{entityName}'.",
                    SessionId: sessionId,
                    Affordances: ["get_entity_detail", "list_instances"]);

            result = existingInstance.EvaluatePolicy(policy);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Evaluation failed: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_policy_expression", "get_entity_detail"]);
        }

        var data = new { policyName, entityName, instanceId, result };

        return new DomainToolResponse(
            Success: true,
            Message: result ? "Policy passed (true)." : "Policy failed (false).",
            SessionId: sessionId,
            Revision: state.Revision,
            Data: data,
            Affordances: ["get_policy_expression", "get_entity_detail"]);
    }

}

/// <summary>
/// Tools for batch DSL operations: apply_dsl (parse + evolve) and export_dsl (print).
/// These provide bulk authoring alongside the unified add/remove incremental tools.
/// </summary>
[McpServerToolType]
internal sealed class DslTool {
    /// <summary>
    /// Applies a Phase 1a/1b .poly DSL text to the session, replacing the current domain.
    /// Parses the text, evolves a fresh domain, and — if analysis succeeds — replaces the
    /// session domain with the result. On failure, returns diagnostics with line/column info.
    /// </summary>
    [McpServerTool(Name = "apply_dsl"), Description(@"Applies Phase 1a/1b .poly DSL text to the session, REPLACING the current domain.

Parses the text, evolves a fresh domain, and — if analysis succeeds — replaces the
session domain with the result. Use this for bulk authoring; incremental single-element
edits go through the unified `add` / `remove` tools (kind + payload).

Supported constructs: entities, properties with constraints (required, unique, range,
length, pattern), lifecycle stages, actions with require gates,
stage subscriptions (when RelName Stage1, Stage2 { effects }), policies, relationships
(N1 navigation properties only: 'orders: many Order' on the source entity),
and effects (transition to, assign, create, create in, entry/exit).

For a complete syntax guide, call `get_dsl_guide` before authoring.
Do not invent constructs from experiment/lab docs — only the shipped surface is accepted.

Unsupported constructs (actor, value, schedule, etc.) produce clear errors.

HONESTY NOTES — what this tool does NOT enforce:
 - Action `when Stage` is parsed and stored but NOT runtime-enforced as a separate
   gate (stage membership comes from placing actions on stages; InvokeAction resolves
   stage-scoped actions from the current stage first, then entity-level fallthrough).
   Use `create_instance` + `invoke_action` (RuntimeTool) to exercise lifecycle.
 - Stage subscriptions are parsed and stored but do NOT auto-fan-out from apply_dsl alone.
   Subscription side-effects need a DomainInstanceStore with registered instances.
   Use `create_instance` + `invoke_action` (RuntimeTool) to trigger subscription fan-out
   on stage transition.
 - apply_dsl replaces the session domain and CLEARS any runtime instances created earlier
   in the session (same as successful evolve — new domain root, fresh instance map).
 - The revision counter becomes the session's current revision + 1, not zero
   (apply_dsl replaces the domain but keeps the session alive).

IMPORTANT: This tool REPLACES the session domain. Incremental single-element edits go
through the unified `add` / `remove` tools (kind + payload).")]
    public static DomainToolResponse ApplyDsl(
        [Description("Session ID returned by create_domain_session")] string sessionId,
        [Description("Phase 1a/1b .poly DSL text to parse and apply")] string polyText) {
        // ── 0. Fail fast on missing session or empty text ─────
        if (!McpSessionStore.TryGet(sessionId, out _))
            return Failure_NotFound(sessionId);

        if (string.IsNullOrWhiteSpace(polyText))
            return new DomainToolResponse(
                Success: false,
                Message: "DSL text is empty. Provide a .poly document with at least a domain header.",
                SessionId: sessionId,
                Affordances: ["get_domain_overview"]);

        // ── 1. Parse ───────────────────────────────────────────
        List<DomainChange> changes;
        DomainSession parseSession;
        try {
            if (!McpSessionStore.TryGet(sessionId, out var parseState))
                return Failure_NotFound(sessionId);

            var seed = parseState.Domain.Extensions.Count > 0
                ? parseState.Domain.Extensions
                : ExtensionCatalog.ProductAuthoring;
            parseSession = DomainSession.ForSource(polyText, seed);
            var parser = new PolyDslParser(polyText, parseSession);
            changes = parser.Parse();
            changes = DomainCompilation.WithSeed(changes, seed).ToList();
        }
        catch (FormatException ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Parse error: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_domain_analysis", "get_domain_overview"]);
        }
        catch (InvalidOperationException ex) {
            return new DomainToolResponse(
                Success: false,
                Message: ex.Message,
                SessionId: sessionId,
                Affordances: ["get_domain_analysis", "get_domain_overview"]);
        }

        // ── 2. Evolve from empty domain (self-contained changes include primitives) ──
        // Extract domain name from changes or use session domain name as fallback.
        var nameChange = changes.OfType<SetDomainNameChange>().FirstOrDefault();
        var domainName = nameChange?.Name ?? "Imported";

        var emptyDomain = new Domain(domainName, []);

        EvolutionResult outcome;
        try {
            outcome = new DomainEvolution(emptyDomain).Apply(changes, session: parseSession);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Evolution failed: {ex.Message}",
                SessionId: sessionId,
                Affordances: ["get_domain_analysis"]);
        }

        if (!outcome.Succeeded) {
            var errorMessages = outcome.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(5)
                .Select(d => d.Message)
                .ToList();

            return new DomainToolResponse(
                Success: false,
                Message: $"Evolution rolled back: {outcome.FailureSummary ?? "Analysis rejected the domain"}",
                SessionId: sessionId,
                Diagnostics: errorMessages.Count > 0 ? errorMessages : null,
                Affordances: ["get_domain_analysis", "get_domain_overview"]);
        }

        // ── 3. Atomically replace the session domain ─────────────
        var replaced = McpSessionStore.Replace(sessionId, outcome.Root, outcome.Analysis);
        if (!replaced) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Session '{sessionId}' not found.",
                Affordances: ["create_domain_session", "list_sessions"]);
        }

        if (!McpSessionStore.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        var entityCount = outcome.Root.Types.OfType<Entity>().Count();
        var relCount = outcome.Analysis.GetAllRelationships(outcome.Root).Count;
        var message = $"Domain '{domainName}' applied: {entityCount} entities, {relCount} relationships.";

        // Build a compact snapshot for the response
        var snapshot = BuildSnapshot(outcome.Root, outcome.Analysis, state.Revision);

        return new DomainToolResponse(
            Success: true,
            Message: message,
            SessionId: sessionId,
            Revision: state.Revision,
            Data: snapshot,
            Affordances: entityCount > 0
                ? ["get_entity_detail", "get_domain_overview", "get_domain_analysis", "create_instance", "evaluate_policy", "invoke_action", "export_dsl", "apply_dsl"]
                : ["get_domain_overview", "add"]);
    }

    /// <summary>
    /// Exports the current session domain as .poly DSL text.
    /// </summary>
    [McpServerTool(Name = "export_dsl"), Description("Exports the current session domain as .poly DSL text using the canonical Phase 1a printer.")]
    public static DomainToolResponse ExportDsl(
        [Description("Session ID")] string sessionId) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        var printer = new DomainDslPrinter(state.Modeling);
        var polyText = printer.Print(state.Domain);

        return new DomainToolResponse(
            Success: true,
            Message: "Domain exported as .poly DSL text.",
            SessionId: sessionId,
            Revision: state.Revision,
            Data: new { poly = polyText },
            Affordances: ["get_domain_overview", "get_entity_detail", "apply_dsl"]);
    }

    /// <summary>
    /// Returns the product-true Phase 1a/1b DSL syntax guide for agents.
    /// Call this before the first large `apply_dsl` to avoid inventing lab constructs.
    /// No session required.
    /// </summary>
    [McpServerTool(Name = "get_dsl_guide"), Description("Returns the product-true Phase 1a/1b DSL syntax guide. Call this before the first large 'apply_dsl' to avoid inventing unsupported lab constructs. No session required.")]
    public static DomainToolResponse GetDslGuide() {
        // Load from embedded resource (packaged with MCP assembly)
        var assembly = typeof(DslTool).Assembly;
        // Try the product guide first (unqualified name), fall back to legacy agent-guide
        string guideText;
        try {
            var stream = assembly.GetManifestResourceStream("Poly.Mcp.Docs.poly-dsl-guide.md")
                       ?? assembly.GetManifestResourceStream("Poly.Mcp.Docs.poly-dsl-agent-guide.md")
                       ?? throw new InvalidOperationException("Embedded resource 'poly-dsl-guide.md' not found.");
            using var reader = new StreamReader(stream);
            guideText = reader.ReadToEnd();
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"Could not load DSL guide: {ex.Message}", Affordances: ["apply_dsl"]);
        }

        return new DomainToolResponse(
            Success: true,
            Message: "DSL syntax guide retrieved.",
            Data: new { guide = guideText },
            Affordances: ["apply_dsl", "add", "create_instance", "evaluate_policy", "invoke_action"]
        );
    }

    // ── Private helpers ─────────────────────────────────────────

    private static object BuildSnapshot(Domain domain, AnalysisResult? analysis, long revision) {
        var entities = domain.Types.OfType<Entity>().Select(e => new {
            name = e.Name,
            propertyCount = e.Properties.Count,
            stageCount = e.Stages.Count,
            actionCount = e.Actions.Count + e.Stages.Sum(s => s.Actions.Count),
            policyCount = e.Policies.Count
        }).ToList();

        var allRelationships = analysis?.GetAllRelationships(domain)
            ?? domain.Types.OfType<Entity>().SelectMany(e => e.Navigations);

        return new {
            domainName = domain.Name,
            revision,
            entityCount = entities.Count,
            relationshipCount = allRelationships.Count(),
            primitiveCount = domain.Types.OfType<PrimitiveType>().Count(),
            hasErrors = analysis?.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ?? false,
            entities,
            relationships = allRelationships.Select(r => new {
                name = r.Name,
                source = r.Source.TypeName,
                target = r.Target.TypeName,
                cardinality = r.Cardinality.ToString()
            }).ToList()
        };
    }

    private static DomainToolResponse Failure_NotFound(string sessionId) =>
        new(Success: false, Message: $"Session '{sessionId}' not found.",
            Affordances: ["create_domain_session", "list_sessions"]);
}