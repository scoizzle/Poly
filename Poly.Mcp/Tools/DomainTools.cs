using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Server;

using Poly.Analysis;
using Poly.Compile;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Queries;
using Poly.Mcp.Sessions;
using Poly.Packs.Product;

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
    [property: JsonPropertyName("hasErrors")] bool HasErrors,
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
    [property: JsonPropertyName("subscriptionPlans")] IReadOnlyList<SubscriptionPlanFact>? SubscriptionPlans = null,
    [property: JsonPropertyName("diagnosticFacts")] IReadOnlyList<DiagnosticFact>? DiagnosticFacts = null,
    [property: JsonPropertyName("actionInvariants")] IReadOnlyList<ActionInvariantFact>? ActionInvariants = null,
    [property: JsonPropertyName("guardClosedProperties")] IReadOnlyList<GuardClosedFact>? GuardClosedProperties = null
);

/// <summary>
/// One action in one stage context (<see cref="StageName"/> is null for a stage-less entity), as the
/// analysis proved it: the guard policies that hold, each property's narrowed range and merged
/// constraints, and the postcondition each assign must respect. Read-only (#367 C-5).
/// </summary>
internal sealed record ActionInvariantFact(
    [property: JsonPropertyName("entityName")] string EntityName,
    [property: JsonPropertyName("actionName")] string ActionName,
    [property: JsonPropertyName("stageName")] string? StageName,
    [property: JsonPropertyName("preconditions")] IReadOnlyList<string> Preconditions,
    [property: JsonPropertyName("properties")] IReadOnlyList<PropertyInvariantFact> Properties,
    [property: JsonPropertyName("postconditions")] IReadOnlyList<PostconditionFact> Postconditions
);

/// <summary>A property inside an action context: numeric bounds (null = unbounded) and constraint text.</summary>
internal sealed record PropertyInvariantFact(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("min")] decimal? Min,
    [property: JsonPropertyName("max")] decimal? Max,
    [property: JsonPropertyName("constraints")] IReadOnlyList<string> Constraints
);

/// <summary>What an assign to <see cref="TargetProperty"/> must satisfy, and the range its value can take.</summary>
internal sealed record PostconditionFact(
    [property: JsonPropertyName("targetProperty")] string TargetProperty,
    [property: JsonPropertyName("declaringAction")] string DeclaringAction,
    [property: JsonPropertyName("min")] decimal? Min,
    [property: JsonPropertyName("max")] decimal? Max,
    [property: JsonPropertyName("constraints")] IReadOnlyList<string> Constraints
);

/// <summary>Properties of one entity whose every writer is proven to keep them within their declared constraints.</summary>
internal sealed record GuardClosedFact(
    [property: JsonPropertyName("entityName")] string EntityName,
    [property: JsonPropertyName("propertyNames")] IReadOnlyList<string> PropertyNames
);

/// <summary>
/// One diagnostic with its anchor: severity, code, and the written element path. The
/// string <c>messages</c> list is the human-readable twin of this; a reader that needs to
/// link a consequence back to the element it came from reads this.
/// </summary>
internal sealed record DiagnosticFact(
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("elementPath")] string ElementPath,
    [property: JsonPropertyName("message")] string Message
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

/// <summary>
/// One finding from a dry-run DSL validation. The code is what a reader categorizes on,
/// and the path anchors it to the written element when the proposal analyzed far enough
/// to keep its tree.
/// </summary>
internal sealed record DslFinding(
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("elementPath")] string ElementPath,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("line"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Line = null,
    [property: JsonPropertyName("column"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Column = null
);

/// <summary>
/// Structured payload for <c>validate_dsl</c>, and for a rejected <c>apply_dsl</c>.
/// Counts describe the proposed domain; a rejected proposal has no accepted tree, so its
/// <see cref="EntityCount"/> and <see cref="RelationshipCount"/> are zero.
/// </summary>
internal sealed record DslValidationData(
    [property: JsonPropertyName("domainName")] string DomainName,
    [property: JsonPropertyName("valid")] bool Valid,
    [property: JsonPropertyName("entityCount")] int EntityCount,
    [property: JsonPropertyName("relationshipCount")] int RelationshipCount,
    [property: JsonPropertyName("errorCount")] int ErrorCount,
    [property: JsonPropertyName("warningCount")] int WarningCount,
    [property: JsonPropertyName("hintCount")] int HintCount,
    [property: JsonPropertyName("findings")] IReadOnlyList<DslFinding> Findings
);

/// <summary>Structured payload for <c>get_domain_report</c>.</summary>
internal sealed record DomainReportData(
    [property: JsonPropertyName("markdown")] string Markdown,
    [property: JsonPropertyName("entityCount")] int EntityCount);

// ── Tool classes ───────────────────────────────────────────────

/// <summary>
/// Tools for managing V3 domain sessions.
/// </summary>
internal sealed class SessionTool(ISessionStore store, ILogger<SessionTool> logger) {
    /// <summary>
    /// Creates a new domain session with the canonical built-in primitive types.
    /// The bootstrapped domain includes canonical primitives (Boolean, Number, Text, Uuid, Binary)
    /// plus Temporal types (Date, Time, DateTime, Duration) loaded implicitly at Load.
    /// Returns a sessionId that must be passed to other tools.
    /// </summary>
    public DomainToolResponse CreateDomainSession(
        [Description("Name for the new domain (e.g. 'Orders', 'Inventory')")] string domainName) {
        if (!DslTokenReader.IsIdentifier(domainName))
            return new DomainToolResponse(
                Success: false,
                Message: EvolveTool.InvalidNameMessage(domainName),
                Affordances: ["create_domain_session"]);
        var (sessionId, state) = store.Create(domainName);
        logger.LogInformation("create_domain_session {Domain} -> {SessionId}", domainName, sessionId);
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
    public DomainToolResponse ListSessions() {
        var sessions = store.ListSessions();
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
internal sealed class QueryTool(ISessionStore store, ILogger<QueryTool> logger) {
    /// <summary>
    /// Returns a high-level overview of the domain: entity/primitive/relationship counts and entity names.
    /// </summary>
    public DomainToolResponse GetDomainOverview(
        [Description("Session ID returned by create_domain_session")] string sessionId) {
        if (!store.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);
        logger.LogDebug("get_domain_overview {SessionId}", sessionId);

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
    public DomainToolResponse GetEntityDetail(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity to inspect")] string entityName) {
        if (!store.TryGet(sessionId, out var state))
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

        // Stage actions live under Stages[].ActionNames; reporting only the entity-level
        // count reads as "0 actions" for an entity whose actions are all stage actions.
        var stageActionCount = detail.Stages.Sum(s => s.ActionNames.Count);
        return new DomainToolResponse(
            Success: true,
            Message: $"Entity '{entityName}': {detail.Properties.Count} properties, {detail.Stages.Count} stages, "
                + $"{detail.Actions.Count} entity-level actions, {stageActionCount} stage actions.",
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
    public DomainToolResponse GetDomainAnalysis(
        [Description("Session ID")] string sessionId) {
        if (!store.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        if (state.LatestAnalysis is null)
            return new DomainToolResponse(
                Success: false,
                Message: "No analysis available.",
                SessionId: sessionId,
                Affordances: ["get_domain_overview"]
            );

        var summary = DomainQueries.GetAnalysisSummary(state.LatestAnalysis, state.Domain);

        // SA′.3: Count hint diagnostics separately from infoCount (Hint ≠ Information severity).
        var hintCount = state.LatestAnalysis.Diagnostics
            .Count(d => d.Severity == DiagnosticSeverity.Hint);

        // ── Structured facts from LatestAnalysis metadata ──

        var entityCount = state.Domain.Types.OfType<Entity>().Count();
        var relationshipCount = state.LatestAnalysis.GetAllRelationships(state.Domain).Count;

        // Root entities from the resolved ownership aggregate: a parentless entity is a
        // root there. The structure bag alone only knows "declares a required reference",
        // which is a different question and reads as non-root for reference-only shapes
        // (Lead.account) that the aggregate resolves as roots.
        var aggregate = state.LatestAnalysis.GetMetadata<OwnershipAggregateMetadata>(state.Domain)?.Aggregate;
        var rootEntityNames = aggregate is not null
            ? aggregate.Entities.Where(e => e.IsRoot).Select(e => e.Name).ToList()
            : state.Domain.Types.OfType<Entity>()
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
        // Aggregate ownership (OwnershipAggregateMetadata) — `aggregate` is read above for roots.
        var aggregates = new List<AggregateFact>();
        if (aggregate is not null) {
            var aggregateByName = aggregate.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
            foreach (var root in aggregate.Entities.Where(e => e.IsRoot).OrderBy(e => e.Name)) {
                // Transitive members: entities whose parent chain leads to this root.
                var members = new List<string>();
                foreach (var candidate in aggregate.Entities.Where(e => !e.IsRoot)) {
                    // Visited guard: a parent chain that never settles (a cycle in the
                    // aggregate) must not spin here; such members are simply not
                    // reachable from this root.
                    var visited = new HashSet<string>(StringComparer.Ordinal) { candidate.Name };
                    var cursor = candidate;
                    while (cursor?.AggregateParentName is not null) {
                        if (string.Equals(cursor.AggregateParentName, root.Name, StringComparison.Ordinal)) {
                            members.Add(candidate.Name);
                            break;
                        }
                        if (!visited.Add(cursor.AggregateParentName))
                            break;
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

        var actionInvariants = new List<ActionInvariantFact>();
        var guardClosed = new List<GuardClosedFact>();
        foreach (var entity in state.Domain.Types.OfType<Entity>().OrderBy(e => e.Name, StringComparer.Ordinal)) {
            var closed = state.LatestAnalysis.GetStructure(entity)?.GuardClosedPropertyNames;
            if (closed is { Count: > 0 })
                guardClosed.Add(new GuardClosedFact(entity.Name, closed.Order(StringComparer.Ordinal).ToList()));

            var actions = entity.Actions.Concat(entity.Stages.SelectMany(s => s.Actions))
                .OrderBy(a => a.Name, StringComparer.Ordinal);
            foreach (var action in actions) {
                var meta = state.LatestAnalysis.GetMetadata<ActionInvariantMetadata>(action);
                if (meta is null) continue;
                foreach (var ctx in meta.StageContexts.OrderBy(c => c.StageName, StringComparer.Ordinal))
                    actionInvariants.Add(ToInvariantFact(entity, action, ctx));
            }
        }

        var data = new AnalysisData(
            summary.ErrorCount, summary.WarningCount, summary.InfoCount, hintCount,
            summary.ErrorCount > 0, summary.Messages,
            EntityCount: entityCount,
            RelationshipCount: relationshipCount,
            RootEntityNames: rootEntityNames.Count > 0 ? rootEntityNames : null,
            CreateInCount: createInCount,
            SubscriptionCount: subscriptionCount,
            ActionSummary: actionSummary?.Count > 0 ? actionSummary : null,
            HasStorageMapping: hasStorage,
            AggregateRootCount: aggregates.Count,
            Aggregates: aggregates.Count > 0 ? aggregates : null,
            SubscriptionPlans: subscriptionPlans.Count > 0 ? subscriptionPlans : null,
            DiagnosticFacts: summary.Findings?.Select(f => new DiagnosticFact(
                f.Severity, f.Code, f.ElementPath, f.Message)).ToList() is { Count: > 0 } facts ? facts : null,
            ActionInvariants: actionInvariants.Count > 0 ? actionInvariants : null,
            GuardClosedProperties: guardClosed.Count > 0 ? guardClosed : null
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

    // Read-only projection of ActionInvariantMetadata. Ordering is fixed: properties by name (ordinal),
    // constraints in the analysis' own order, postconditions in effect order, preconditions in policy order.
    private static ActionInvariantFact ToInvariantFact(Entity entity, Poly.DomainModeling.Ontology.Action action, ActionStageInvariant ctx) {
        var properties = ctx.MergedConstraints.Keys
            .Concat(ctx.NarrowedRanges.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(name => {
                ctx.NarrowedRanges.TryGetValue(name, out var range);
                ctx.MergedConstraints.TryGetValue(name, out var merged);
                return new PropertyInvariantFact(
                    name, range?.Min, range?.Max,
                    (merged ?? []).Select(DescribeConstraint).ToList());
            })
            .Where(p => p.Min is not null || p.Max is not null || p.Constraints.Count > 0)
            .ToList();
        var postconditions = ctx.Postconditions.Select(p => new PostconditionFact(
            p.TargetProperty, p.DeclaringAction.Name, p.ValueRange?.Min, p.ValueRange?.Max,
            p.Constraints.Select(DescribeConstraint).ToList())).ToList();
        return new ActionInvariantFact(
            entity.Name, action.Name, ctx.StageName,
            ctx.Preconditions.Select(p => p.Name).ToList(), properties, postconditions);
    }

    private static string DescribeConstraint(Constraint c) =>
        ConstraintKinds.Of(c)?.Describe(c) ?? c.GetType().Name;

    /// <summary>
    /// Returns a human-readable Markdown report of the domain. A projection over the same
    /// facts the machine exports read, so the two cannot drift.
    /// </summary>
    public DomainToolResponse GetDomainReport(
        [Description("Session ID")] string sessionId) {
        if (!store.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);
        logger.LogDebug("get_domain_report {SessionId}", sessionId);

        if (state.LatestAnalysis is null)
            return new DomainToolResponse(
                Success: false,
                Message: "No analysis available.",
                SessionId: sessionId,
                Affordances: ["apply_dsl", "validate_dsl", "get_domain_overview"]);

        if (state.LatestAnalysis.HasErrors)
            return new DomainToolResponse(
                Success: false,
                Message: "Domain has analysis errors; no report was generated.",
                SessionId: sessionId,
                Diagnostics: state.LatestAnalysis.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)
                    .ToList(),
                Affordances: ["get_domain_analysis", "validate_dsl"]);

        try {
            var markdown = DomainReportExporter.Export(state.Domain);
            var entityCount = state.Domain.Types.OfType<Entity>().Count();
            return new DomainToolResponse(
                Success: true,
                Message: $"Domain report generated for '{state.Domain.Name}': {entityCount} entities.",
                SessionId: sessionId,
                Revision: state.Revision,
                Data: new DomainReportData(markdown, entityCount),
                Affordances: ["get_domain_overview", "get_entity_detail", "export_dsl"]);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Domain report failed: {ex.Message}",
                SessionId: sessionId,
                Revision: state.Revision,
                Data: new { error = ex.Message },
                Affordances: ["get_domain_analysis"]);
        }
    }

    /// <summary>
    /// Returns authoring suggestions (advisory hints) for the current domain.
    /// Suggestions identify common gaps like missing stages, actions, or policies.
    /// </summary>
    public DomainToolResponse GetDomainSuggestions(
        [Description("Session ID")] string sessionId) {
        if (!store.TryGet(sessionId, out var state))
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
    public DomainToolResponse GetRelationships(
        [Description("Session ID")] string sessionId,
        [Description("Optional entity name filter — only show relationships involving this entity")] string? entityName = null) {
        if (!store.TryGet(sessionId, out var state))
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
internal sealed class EvolveTool(ISessionStore store, ILogger<EvolveTool> logger) {

    // ── Unified add (kind + payload) ────────────────────────────

    /// <summary>
    /// Creates one domain definition element, dispatched by <c>kind</c> + <c>payload</c>.
    /// Incremental structure edits only — bulk structure, effects, and subscriptions go
    /// through <c>apply_dsl</c>. Expression bodies are product DSL text, never JSON IR.
    /// </summary>
    public DomainToolResponse Add(
        [Description("Session ID returned by create_domain_session")] string sessionId,
        [Description("Domain element kind (case-sensitive): entity, property, stage, action, stage_action, relationship, constraint, policy, value_type, contract, contract_value_type, contract_endpoint, contract_binding")] string kind,
        [Description("JSON object of kind-specific fields. Shapes are in get_dsl_guide section 12.")] string payload) {
        if (!store.TryGet(sessionId, out var state))
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
                    if (!DslTokenReader.IsIdentifier(name)) return InvalidName(sessionId, name);
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
                    var primitives = state.Domain.Types.OfType<PrimitiveType>().Select(t => t.Name).ToHashSet();
                    if (!DslTokenReader.IsPropertyName(name, typeName, primitives.Contains)) return InvalidName(sessionId, name);
                    return Evolve(sessionId, builder =>
                            builder.AddPropertyToEntity(entityName, new Property(name, new DomainTypeReference(typeName), [])),
                        successAffordances: ["add", "apply_dsl", "get_entity_detail"]);
                }
            case "stage": {
                    var entityName = Field(root, "entityName");
                    var name = Field(root, "name");
                    if (entityName is null) return MissingField(sessionId, kind, "entityName");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    if (!DslTokenReader.IsIdentifier(name)) return InvalidName(sessionId, name);
                    return Evolve(sessionId, builder => builder.AddStage(entityName, name),
                        successAffordances: ["add", "apply_dsl", "get_entity_detail"]);
                }
            case "action": {
                    var entityName = Field(root, "entityName");
                    var name = Field(root, "name");
                    if (entityName is null) return MissingField(sessionId, kind, "entityName");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    if (!DslTokenReader.IsIdentifier(name)) return InvalidName(sessionId, name);
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
                    if (!DslTokenReader.IsIdentifier(name)) return InvalidName(sessionId, name);
                    return Evolve(sessionId, builder => builder.AddActionToStage(entityName, stageName, name),
                        successAffordances: ["add", "apply_dsl", "get_entity_detail"]);
                }
            case "relationship": {
                    var name = Field(root, "name");
                    var source = Field(root, "source", "sourceEntityName");
                    var target = Field(root, "target", "targetEntityName");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    if (!DslTokenReader.IsIdentifier(name)) return InvalidName(sessionId, name);
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
                    if (!DslTokenReader.IsIdentifier(name)) return InvalidName(sessionId, name);
                    if (expression is null) return MissingField(sessionId, kind, "expression");
                    return AddPolicyCore(sessionId, entityName, name, expression);
                }
            case "value_type": {
                    var name = Field(root, "name");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    if (!DslTokenReader.IsIdentifier(name)) return InvalidName(sessionId, name);
                    return Evolve(sessionId, builder => builder.AddValueType(name),
                        successAffordances: ["add", "apply_dsl", "get_domain_overview"]);
                }
            case "contract": {
                    var name = Field(root, "name");
                    var source = Field(root, "source", "sourceIdentifier");
                    var version = Field(root, "version");
                    if (name is null) return MissingField(sessionId, kind, "name");
                    if (!DslTokenReader.IsIdentifier(name)) return InvalidName(sessionId, name);
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
                    if (!DslTokenReader.IsIdentifier(name)) return InvalidName(sessionId, name);
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
                    if (!DslTokenReader.IsIdentifier(name)) return InvalidName(sessionId, name);
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
                    if (!DslTokenReader.IsIdentifier(name)) return InvalidName(sessionId, name);
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

    private DomainToolResponse AddConstraintCore(
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

    internal DomainToolResponse AddPolicyCore(
        string sessionId, string entityName, string policyName, string expressionDsl) {
        // Session-first (consistent with other tools); parse with the session's
        // Same session tables as apply_dsl so concept folds (Now, durations) agree.
        if (!store.TryGet(sessionId, out var state))
            return new DomainToolResponse(
                Success: false,
                Message: $"Session '{sessionId}' not found.",
                Affordances: ["create_domain_session", "list_sessions"]);

        DomainExpression expr;
        try {
            expr = DslExpressionFragment.Resolve(
                DslExpressionFragment.ParseExpressionFragment(expressionDsl), state.Modeling);
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

    internal static string InvalidNameMessage(string? name) =>
        $"Name '{name}' is not a valid name: use a letter or '_' followed by letters, digits or '_', and not a DSL keyword "
        + "(a property may also be named Text, Number or Boolean when its type is a primitive). This is the rule apply_dsl uses.";

    private static DomainToolResponse InvalidName(string sessionId, string name) =>
        new(Success: false,
            Message: InvalidNameMessage(name),
            SessionId: sessionId,
            Affordances: ["add", "get_domain_overview"]);

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
    public DomainToolResponse Remove(
        [Description("Session ID returned by create_domain_session")] string sessionId,
        [Description("Domain element kind (case-sensitive): entity, property, stage, action, stage_action, relationship, policy")] string kind,
        [Description("JSON object of identity fields. Shapes are in get_dsl_guide section 12.")] string payload) {
        if (!store.TryGet(sessionId, out _))
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
                    if (source is null && store.TryGet(sessionId, out var state) && state.Domain is not null) {
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
    /// Builds a structural fingerprint for no-op detection: type and navigation counts,
    /// each entity's name with its property/constraint/stage/action/policy counts,
    /// each imported contract's name with its value-type and endpoint counts, and the
    /// contract binding count. An evolve that leaves it unchanged is reported as
    /// "No changes applied". No add or remove path is known to reach that today;
    /// missing targets are refused by evolution.
    /// </summary>
    internal static string GetFingerprint(Domain domain) {
        var contracts = string.Join(",", domain.ImportedContracts.OrderBy(c => c.Name)
            .Select(c => $"{c.Name}({c.Types.Count}t,{c.Endpoints.Count}e)"));
        var typeCounts = $"T:{domain.Types.Count}|R:{domain.Types.OfType<Entity>().SelectMany(e => e.Navigations).Count()}"
            + $"|C:[{contracts}]|B:{domain.ContractBindings.Count}";
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

    private DomainToolResponse Evolve(
        string sessionId,
        Func<EvolutionBuilder, EvolutionBuilder> mutate,
        IReadOnlyList<string>? successAffordances = null) {
        // Snapshot read for session-not-found check and fingerprint.
        // The actual mutation happens atomically inside store.Evolve.
        if (!store.TryGet(sessionId, out var snapshot))
            return new DomainToolResponse(
                Success: false,
                Message: $"Session '{sessionId}' not found.",
                Affordances: ["create_domain_session", "list_sessions"]);
        logger.LogDebug("evolve {SessionId}", sessionId);

        var before = GetFingerprint(snapshot.Domain);
        var preRevision = snapshot.Revision;

        var outcome = store.Evolve(sessionId, (domain, modeling, priorAnalysis) => {
            var result = new DomainEvolution(domain).Evolve();
            result = mutate(result);
            // Hand the analysis of the domain being changed to the evolution gate. Without it the
            // gate analyzes the live domain to find its mutation targets, and that analysis binds
            // Modeling into RuntimeAnalysisCache, evicting the module the executor is running —
            // so a rejected change disabled `unique` for the rest of the session (#387).
            return result.Apply(priorAnalysis: priorAnalysis, session: modeling);
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

        // store.Evolve already committed the update atomically.
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
    public DomainToolResponse GetConstraints(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity")] string entityName,
        [Description("Optional property name filter")] string? propertyName = null) {
        if (!store.TryGet(sessionId, out var state))
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
            "Optional" => new OptionalConstraint(),
            "Length" => new LengthConstraint(
                cfg?.TryGetValue("min", out var lmin) == true ? lmin.GetInt32() : 0,
                cfg?.TryGetValue("max", out var lmax) == true ? lmax.GetInt32() : int.MaxValue),
            "Pattern" => new PatternConstraint(
                cfg is not null && (cfg.TryGetValue("pattern", out var p) || cfg.TryGetValue("regex", out p))
                    ? p.GetString()!
                    : throw new ArgumentException("Pattern requires 'pattern' config.")),
            "Unique" => new UniqueConstraint(),
            _ => throw new ArgumentException($"Unknown constraint type '{type}'. Supported: Range, Required, Optional, Length, Pattern, Unique.")
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
        OptionalConstraint => "Optional",
        LengthConstraint l => $"Length(min={l.MinLength}, max={l.MaxLength})",
        PatternConstraint p => $"Pattern({p.Pattern})",
        UniqueConstraint => "Unique",
        _ => c.GetType().Name
    };
}

/// <summary>
/// Tools for inspecting and evaluating policy guards on domain entities.
/// <c>get_policy_expression</c> is inspect-only; <c>evaluate_policy</c> runs
/// against an instance from <c>create_instance</c>.
/// </summary>
internal sealed class PolicyTool(ISessionStore store, ILogger<PolicyTool> logger) {
    /// <summary>
    /// Returns the guard expression text of a named policy on an entity.
    /// Use this to inspect what condition a policy enforces (e.g. "Age >= 18").
    /// </summary>
    public DomainToolResponse GetPolicyExpression(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity that has the policy")] string entityName,
        [Description("Name of the policy to inspect")] string policyName) {
        if (!store.TryGet(sessionId, out var state))
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

        var expressionText = new DomainDslPrinter(state.Modeling).PrintExpressionText(policy.Expression);
        return new DomainToolResponse(
            Success: true,
            Message: $"Policy '{policyName}' on '{entityName}': {expressionText}",
            SessionId: sessionId,
            Revision: state.Revision,
            Data: new { policyName = policy.Name, entityName = entity.Name, expression = expressionText },
            Affordances: ["get_entity_detail", "get_domain_overview"]
        );
    }

    /// <summary>
    /// Evaluates a named policy on a store instance from <c>create_instance</c>.
    /// </summary>
    public DomainToolResponse EvaluatePolicy(
        [Description("Session ID")] string sessionId,
        [Description("Name of the entity that has the policy")] string entityName,
        [Description("Name of the policy to evaluate")] string policyName,
        [Description("Instance ID from create_instance. Required.")] string instanceId) {
        if (!store.TryGet(sessionId, out var state))
            return new DomainToolResponse(
                Success: false,
                Message: $"Session '{sessionId}' not found.",
                Affordances: ["create_domain_session", "list_sessions"]);
        logger.LogDebug("evaluate_policy {SessionId} {Entity}.{Policy}", sessionId, entityName, policyName);

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
            if (state.ExecutorIfCreated is not { } executor
                || !executor.TryGet(instanceId, out var existingInstance) || existingInstance is null)
                return new DomainToolResponse(
                    Success: false,
                    Message: $"Instance '{instanceId}' not found in session. Create it first via create_instance.",
                    SessionId: sessionId,
                    Affordances: ["create_instance", "list_instances"]);

            if (!string.Equals(existingInstance.EntityName, entityName, StringComparison.Ordinal))
                return new DomainToolResponse(
                    Success: false,
                    Message: $"Instance '{instanceId}' is for entity '{existingInstance.EntityName}', not '{entityName}'.",
                    SessionId: sessionId,
                    Affordances: ["get_entity_detail", "list_instances"]);

            result = executor.EvaluatePolicy(existingInstance, policy.Name);
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
/// Tools for batch DSL operations: validate_dsl (dry run), apply_dsl (parse + evolve),
/// and export_dsl (print). These provide bulk authoring alongside the unified add/remove
/// incremental tools.
/// </summary>
internal sealed class DslTool(ISessionStore store, ILogger<DslTool> logger) {
    /// <summary>
    /// Validates a .poly document without touching the session: parse, evolve a fresh
    /// domain, and analyze it. This is the iteration half of the authoring loop — an
    /// agent can fix a rejected document without losing the applied domain or instances.
    /// </summary>
    public DomainToolResponse ValidateDsl(
        [Description("Session ID returned by create_domain_session")] string sessionId,
        [Description("Phase 1a/1b .poly DSL text to validate; the session domain is not changed")] string polyText) {
        if (!store.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);
        logger.LogDebug("validate_dsl {SessionId}", sessionId);

        if (string.IsNullOrWhiteSpace(polyText))
            return DslFailure(sessionId, "DSL text is empty. Provide a .poly document with at least a domain header.");

        var compile = CompileDsl(state, polyText);
        if (!compile.Succeeded)
            return Rejected(sessionId, compile, state.Revision);

        var data = BuildValidationData(compile);
        return new DomainToolResponse(
            Success: data.Valid,
            Message: data.Valid
                ? $"Document is valid: {data.EntityCount} entities, {data.RelationshipCount} relationships, {data.WarningCount} warning(s)."
                : $"Document is invalid: {data.ErrorCount} error(s), {data.WarningCount} warning(s). Fix the findings and validate again.",
            SessionId: sessionId,
            Revision: state.Revision,
            Data: data,
            Diagnostics: data.Findings.Count > 0
                ? data.Findings.Select(FormatFinding).ToList()
                : null,
            Affordances: data.Valid
                ? ["apply_dsl", "export_dsl"]
                : ["get_dsl_guide", "validate_dsl"]);
    }

    /// <summary>
    /// Applies a Phase 1a/1b .poly DSL text to the session, replacing the current domain.
    /// Parses the text, evolves a fresh domain, and — if analysis succeeds — replaces the
    /// session domain with the result. On failure nothing changes and the response carries
    /// the same structured findings as <c>validate_dsl</c>.
    /// </summary>
    public DomainToolResponse ApplyDsl(
        [Description("Session ID returned by create_domain_session")] string sessionId,
        [Description("Phase 1a/1b .poly DSL text to parse and apply")] string polyText) =>
        ApplyDslAtRevision(sessionId, polyText, expectedRevision: null);

    /// <summary>
    /// <see cref="ApplyDsl"/> that also writes nothing when the session has moved past
    /// <paramref name="expectedRevision"/>, so a read-modify-write caller cannot lose an update.
    /// </summary>
    public DomainToolResponse ApplyDslAtRevision(string sessionId, string polyText, long? expectedRevision) {
        // ── 0. Fail fast on missing session or empty text ─────
        if (!store.TryGet(sessionId, out var parseState))
            return Failure_NotFound(sessionId);
        logger.LogDebug("apply_dsl {SessionId}", sessionId);

        if (string.IsNullOrWhiteSpace(polyText))
            return DslFailure(sessionId, "DSL text is empty. Provide a .poly document with at least a domain header.");

        // ── 1. Parse and analyze a fresh domain (no session mutation yet) ──
        var compile = CompileDsl(parseState, polyText);
        if (!compile.Succeeded)
            return Rejected(sessionId, compile, parseState.Revision);

        var domainName = compile.DomainName;

        // ── 2. Atomically replace the session domain ─────────────
        var applied = compile.Core;
        var replaced = store.Replace(
            sessionId, applied.Domain!, applied.Analysis!, applied.Session!, polyText, expectedRevision);
        if (!replaced) {
            if (expectedRevision is not null && store.TryGet(sessionId, out _))
                return new DomainToolResponse(
                    Success: false,
                    Message: $"Session '{sessionId}' changed since revision {expectedRevision}; nothing was written.",
                    SessionId: sessionId);
            return new DomainToolResponse(
                Success: false,
                Message: $"Session '{sessionId}' not found.",
                Affordances: ["create_domain_session", "list_sessions"]);
        }

        if (!store.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        var entityCount = applied.Domain!.Types.OfType<Entity>().Count();
        var relCount = applied.Analysis!.GetAllRelationships(applied.Domain).Count;
        var message = $"Domain '{domainName}' applied: {entityCount} entities, {relCount} relationships.";

        // Build a compact snapshot for the response
        var snapshot = BuildSnapshot(applied.Domain, applied.Analysis, state.Revision);

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
    public DomainToolResponse ExportDsl(
        [Description("Session ID")] string sessionId) {
        if (!store.TryGet(sessionId, out var state))
            return Failure_NotFound(sessionId);

        // The shipped DSL has no ManyToMany syntax (the incremental add tool can
        // author one in-session). Degrading the cardinality to `many` would silently
        // change the domain on round-trip — fail closed and name the relationship.
        var manyToMany = state.Domain.Types
            .OfType<Entity>()
            .SelectMany(e => e.Navigations)
            .FirstOrDefault(n => n.Cardinality == RelationshipCardinality.ManyToMany);
        if (manyToMany is not null)
            return new DomainToolResponse(
                Success: false,
                Message: $"Cannot export: relationship '{manyToMany.Name}' is ManyToMany, " +
                    "which the shipped DSL cannot express. Round-tripping would silently " +
                    "degrade it to one-to-many.",
                SessionId: sessionId,
                Revision: state.Revision,
                Affordances: ["get_relationships", "get_domain_overview"]);

        // Not Options.ImplicitIds: apply_dsl replaces Modeling with a session seeded from the previous extensions.
        var printer = new DomainDslPrinter(state.Modeling, ProductCatalog.ImplicitIds);
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
    public DomainToolResponse GetDslGuide(
        [Description("Omit for the short guide. A heading number or title returns that section. \"all\" returns the full guide.")] string? section = null) {
        var assembly = typeof(DslTool).Assembly;
        string guideText;
        try {
            var stream = assembly.GetManifestResourceStream("Poly.Mcp.Docs.poly-dsl-guide.md")
                       ?? throw new InvalidOperationException("Embedded resource 'poly-dsl-guide.md' not found.");
            using var reader = new StreamReader(stream);
            guideText = reader.ReadToEnd();
            var selected = SelectGuide(guideText, section);
            if (selected is null) {
                return new DomainToolResponse(
                    Success: false,
                    Message: "Unknown guide section '" + section + "'. " + GuideIndex(SplitGuide(guideText)),
                    Affordances: ["get_dsl_guide"]);
            }

            guideText = selected;
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

    private readonly record struct GuideSection(string Number, string Title, string Text);

    private static string? SelectGuide(string guide, string? section) {
        if (string.IsNullOrWhiteSpace(section))
            return ShortGuide(guide);
        if (section.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            return guide;

        // A number names a heading number only. A title tries an exact match before a
        // substring, so a short word does not land on a longer heading that contains it.
        var want = section.Trim();
        var sections = SplitGuide(guide);
        if (IsAllDigits(want))
            return sections.FirstOrDefault(s => s.Number == want).Text;
        var exact = sections.FirstOrDefault(s => s.Title.Equals(want, StringComparison.OrdinalIgnoreCase));
        if (exact.Text is not null)
            return exact.Text;
        return sections.FirstOrDefault(s => s.Title.Contains(want, StringComparison.OrdinalIgnoreCase)).Text;
    }

    private static string ShortGuide(string guide) {
        var sections = SplitGuide(guide);
        var principles = sections.FirstOrDefault(s => s.Number == "0");
        var denied = sections.FirstOrDefault(s => s.Number == "10");
        if (string.IsNullOrEmpty(principles.Text) || string.IsNullOrEmpty(denied.Text))
            throw new InvalidOperationException("DSL guide is missing section 0 or section 10.");

        const string marker = "**Golden workflow:**";
        var goldenAt = guide.IndexOf(marker, StringComparison.Ordinal);
        if (goldenAt < 0)
            throw new InvalidOperationException("DSL guide is missing the golden workflow.");
        var goldenEnd = guide.IndexOf("\n\n", goldenAt, StringComparison.Ordinal);
        var golden = goldenEnd < 0 ? guide[goldenAt..] : guide[goldenAt..goldenEnd];

        var sb = new StringBuilder();
        sb.AppendLine("# Poly DSL — short guide");
        sb.AppendLine();
        sb.AppendLine("Pass `section` as a number below, or `all`, to `get_dsl_guide` for the rest. Shapes for `add` and `remove` are section 12.");
        sb.AppendLine();
        sb.AppendLine(principles.Text);
        sb.AppendLine();
        sb.AppendLine(denied.Text);
        sb.AppendLine();
        sb.AppendLine(golden.TrimEnd());
        sb.AppendLine();
        sb.Append(GuideIndex(sections));
        return sb.ToString();
    }

    private static string GuideIndex(List<GuideSection> sections) {
        var sb = new StringBuilder();
        sb.AppendLine("## Sections");
        foreach (var part in sections)
            sb.Append("- ").Append(part.Number).Append(". ").AppendLine(part.Title);
        return sb.ToString().TrimEnd();
    }

    private static List<GuideSection> SplitGuide(string guide) {
        var marks = new List<int>();
        if (guide.StartsWith("## ", StringComparison.Ordinal))
            marks.Add(0);
        var search = 0;
        while (true) {
            var idx = guide.IndexOf("\n## ", search, StringComparison.Ordinal);
            if (idx < 0)
                break;
            marks.Add(idx + 1);
            search = idx + 4;
        }

        var sections = new List<GuideSection>(marks.Count);
        for (var i = 0; i < marks.Count; i++) {
            var from = marks[i];
            var to = i + 1 < marks.Count ? marks[i + 1] : guide.Length;
            var text = guide[from..to].TrimEnd();
            var newline = text.IndexOf('\n');
            var heading = newline < 0 ? text : text[..newline];
            var number = "";
            var title = heading;
            if (heading.StartsWith("## ", StringComparison.Ordinal)) {
                var rest = heading[3..];
                var dot = rest.IndexOf('.');
                if (dot > 0 && IsAllDigits(rest.AsSpan(0, dot))) {
                    number = rest[..dot];
                    title = rest[(dot + 1)..].Trim();
                }
            }
            sections.Add(new GuideSection(number, title, text));
        }

        return sections;
    }

    private static bool IsAllDigits(ReadOnlySpan<char> text) {
        if (text.Length == 0)
            return false;
        foreach (var c in text) {
            if (!char.IsAsciiDigit(c))
                return false;
        }
        return true;
    }

    // ── Private helpers ─────────────────────────────────────────

    /// <summary>
    /// The parse-and-analyze half shared by <c>validate_dsl</c> and <c>apply_dsl</c>, run by the
    /// shared <see cref="PolyCompiler"/> core. <see cref="Error"/> is set when the document never
    /// reached analysis (parse, unknown or duplicate <c>uses</c>, evolution threw); otherwise
    /// <see cref="Core"/> carries the analysis gate's result and every diagnostic.
    /// </summary>
    private sealed record DslCompileResult(Poly.Compile.CompileResult Core, string DomainName, string? Error) {
        public bool Succeeded => Core.Succeeded;

        /// <summary>Up to three error messages, in report order.</summary>
        public string FailureSummary => string.Join("; ",
            Core.Diagnostics.Where(d => d.IsError).Select(d => d.Message).Take(3));
    }

    private static DslCompileResult CompileDsl(McpSessionState state, string polyText) {
        var seed = state.Domain.Extensions.Count > 0
            ? state.Domain.Extensions
            : ProductCatalog.ImplicitIds;
        // The harness catalog loads the libraries without host-file contributors; the check
        // and produce rules are the ones polyc check and polyc build run.
        var compiler = new PolyCompiler(
            ProductCatalog.Model, seed, new CompileHooks { DefaultDomainName = "Imported" });
        var core = compiler.Analyze(polyText, "<workspace>");
        if (core.Analysis is null)
            return new DslCompileResult(core, core.DomainName ?? "", RejectedBeforeAnalysis(core));
        return new DslCompileResult(core, core.DomainName ?? "Imported", null);
    }

    private static string RejectedBeforeAnalysis(Poly.Compile.CompileResult core) {
        var errors = core.Diagnostics.Where(d => d.IsError).ToList();
        if (errors.Count == 0)
            return "Analysis rejected the domain.";
        return string.Join("; ", errors.Select(d => {
            var where = d.Placed ? $" (line {d.Line}, col {d.Column})" : "";
            return d.Code is DiagnosticCodes.Parse or DiagnosticCodes.DuplicateExtension
                ? $"Parse error: {d.Message}{where}"
                : d.Message;
        }));
    }

    private static string CapitalSeverity(string severity) => severity switch {
        Severities.Error => "Error",
        Severities.Warning => "Warning",
        Severities.Information => "Information",
        _ => "Hint",
    };

    /// <summary>
    /// A rejected document. The summary is the flattened text; findings keep the core's code,
    /// path, and span. Do not collapse those into the summary.
    /// </summary>
    private static DomainToolResponse Rejected(string sessionId, DslCompileResult compile, long revision) {
        var data = BuildValidationData(compile);
        var summary = compile.Error
            ?? (compile.FailureSummary.Length > 0 ? compile.FailureSummary : "Analysis rejected the domain.");
        return new DomainToolResponse(
            Success: false,
            Message: summary,
            SessionId: sessionId,
            Revision: revision,
            Data: data,
            Diagnostics: data.Findings.Select(FormatFinding).ToList(),
            Affordances: ["get_dsl_guide", "validate_dsl"]);
    }

    /// <summary>
    /// Projects the core's diagnostics into the response payload, in severity order, with the
    /// source line and column when the finding was placed. Every severity is included.
    /// </summary>
    private static DslValidationData BuildValidationData(DslCompileResult compile) {
        var core = compile.Core;
        static DslFinding Finding(Poly.Compile.CompileDiagnostic d) => new(
            CapitalSeverity(d.Severity), d.Code, d.Path, d.Message,
            d.Placed ? d.Line : null, d.Placed ? d.Column : null);

        var errors = core.Diagnostics.Where(d => d.IsError).ToList();
        var warnings = core.Diagnostics.Where(d => d.IsWarning).ToList();
        var information = core.Diagnostics.Where(d => d.Severity == Severities.Information).ToList();
        var hints = core.Diagnostics.Where(d => d.Severity == Severities.Hint).ToList();
        var findings = errors.Concat(warnings).Concat(information).Concat(hints).Select(Finding).ToList();

        // Fail closed: a rejected document must never read as "invalid with 0 errors".
        var rejectedWithoutError = !core.Succeeded && errors.Count == 0;
        if (rejectedWithoutError)
            findings.Insert(0, new DslFinding(
                "Error", null, "", "The proposal was rejected, but no analysis or mutation error was reported."));

        var entityCount = core.Succeeded ? core.Domain!.Types.OfType<Entity>().Count() : 0;
        var relationshipCount = core.Succeeded
            ? core.Analysis!.GetAllRelationships(core.Domain!).Count
            : 0;

        return new DslValidationData(
            DomainName: compile.DomainName,
            Valid: core.Succeeded,
            EntityCount: entityCount,
            RelationshipCount: relationshipCount,
            ErrorCount: errors.Count + (rejectedWithoutError ? 1 : 0),
            WarningCount: warnings.Count,
            HintCount: hints.Count,
            Findings: findings);
    }

    private static string FormatFinding(DslFinding finding) {
        var anchor = string.IsNullOrEmpty(finding.ElementPath) ? "" : $" {finding.ElementPath}:";
        return finding.Code is null
            ? $"[{finding.Severity}] {finding.Message}"
            : $"[{finding.Severity}] {finding.Code}{anchor} {finding.Message}";
    }

    private static DomainToolResponse DslFailure(string sessionId, string message) {
        var finding = new DslFinding("Error", null, "", message);
        return new DomainToolResponse(
            Success: false,
            Message: message,
            SessionId: sessionId,
            Data: new DslValidationData("", false, 0, 0, 1, 0, 0, [finding]),
            Diagnostics: [message],
            Affordances: ["get_dsl_guide", "validate_dsl"]);
    }

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