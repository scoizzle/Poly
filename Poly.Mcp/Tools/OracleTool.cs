using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ModelContextProtocol.Server;

using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Queries;
using Poly.Interpretation.CSharp;
using Poly.Mcp.Sessions;

namespace Poly.Mcp.Tools;

/// <summary>
/// Oracle tools: visibility, lowering, description, and a DSL-fragment probe.
/// These tools are read-only — they never mutate session state. Named-policy
/// and named-action simulate is <c>evaluate_policy</c> / <c>invoke_action</c>.
/// </summary>
[McpServerToolType]
internal sealed class OracleTool {
    // ── Shared helpers ──────────────────────────────────────────

    private static DomainToolResponse? TryParseDslExpression(string expressionDsl, out DomainExpression expr) {
        expr = null!;
        try {
            // Product authoring tables (temporal + storage) — same folds as add(kind: policy).
            expr = DslExpressionFragment.ParseExpressionFragment(expressionDsl, ExtensionCatalog.Core.Authoring);
            return null;
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"Invalid expression: {ex.Message}", Data: new { parseError = ex.Message }, Affordances: []);
        }
    }

    private static DescribeExpressionData DescribeExpression(DomainExpression expr) {
        var structured = DescribeStructured(expr, 0);
        var plainEnglish = DescribePlainEnglish(expr);
        return new DescribeExpressionData(structured, plainEnglish);
    }

    private static string DescribeStructured(DomainExpression expr, int indent) {
        var pad = new string(' ', indent * 2);
        return expr switch {
            PropertyAccess p => $"{pad}Property: {p.Name}",
            ParameterAccess p => $"{pad}Parameter: {p.Name}",
            Literal l => $"{pad}Literal: {l.Value ?? "null"}",
            OwnedAccess oa => $"{pad}Owned: {oa.OwnedName}\n{DescribeStructured(oa.Inner, indent + 1)}",
            RelationshipNavigation rn => $"{pad}Nav: {rn.RelationshipName}\n{DescribeStructured(rn.TargetProperty, indent + 1)}",
            Comparison c => $"{pad}{OpName(c.Kind)}\n{DescribeStructured(c.Left, indent + 1)}\n{DescribeStructured(c.Right, indent + 1)}",
            And a => $"{pad}And\n{DescribeStructured(a.Left, indent + 1)}\n{DescribeStructured(a.Right, indent + 1)}",
            Or o => $"{pad}Or\n{DescribeStructured(o.Left, indent + 1)}\n{DescribeStructured(o.Right, indent + 1)}",
            Not n => $"{pad}Not\n{DescribeStructured(n.Operand, indent + 1)}",
            Add a => $"{pad}Add\n{DescribeStructured(a.Left, indent + 1)}\n{DescribeStructured(a.Right, indent + 1)}",
            Subtract s => $"{pad}Subtract\n{DescribeStructured(s.Left, indent + 1)}\n{DescribeStructured(s.Right, indent + 1)}",
            Multiply m => $"{pad}Multiply\n{DescribeStructured(m.Left, indent + 1)}\n{DescribeStructured(m.Right, indent + 1)}",
            Divide d => $"{pad}Divide\n{DescribeStructured(d.Left, indent + 1)}\n{DescribeStructured(d.Right, indent + 1)}",
            Exists e => $"{pad}Exists\n{DescribeStructured(e.Target, indent + 1)}",
            NotExists ne => $"{pad}NotExists\n{DescribeStructured(ne.Target, indent + 1)}",
            DateOperation d => $"{pad}DateOp ({d.Kind})\n{DescribeStructured(d.Date, indent + 1)}\n{DescribeStructured(d.Offset, indent + 1)}",
            _ => $"{pad}{expr.GetType().Name}: {expr}"
        };
    }

    private static string DescribePlainEnglish(DomainExpression expr) => expr switch {
        PropertyAccess p => $"the value of '{p.Name}'",
        ParameterAccess p => $"parameter '{p.Name}'",
        Literal l => FormatLiteral(l.Value),
        OwnedAccess oa => $"{DescribePlainEnglish(oa.Inner)} of '{oa.OwnedName}'",
        RelationshipNavigation rn => $"{DescribePlainEnglish(rn.TargetProperty)} via '{rn.RelationshipName}'",
        Comparison c => $"{DescribePlainEnglish(c.Left)} {OpEnglish(c.Kind)} {DescribePlainEnglish(c.Right)}",
        And a => $"{DescribePlainEnglish(a.Left)} and {DescribePlainEnglish(a.Right)}",
        Or o => $"{DescribePlainEnglish(o.Left)} or {DescribePlainEnglish(o.Right)}",
        Not n => $"not ({DescribePlainEnglish(n.Operand)})",
        Add a => $"{DescribePlainEnglish(a.Left)} plus {DescribePlainEnglish(a.Right)}",
        Subtract s => $"{DescribePlainEnglish(s.Left)} minus {DescribePlainEnglish(s.Right)}",
        Multiply m => $"{DescribePlainEnglish(m.Left)} times {DescribePlainEnglish(m.Right)}",
        Divide d => $"{DescribePlainEnglish(d.Left)} divided by {DescribePlainEnglish(d.Right)}",
        Exists e => $"{DescribePlainEnglish(e.Target)} exists",
        NotExists ne => $"{DescribePlainEnglish(ne.Target)} does not exist",
        DateOperation d => $"date operation ({d.Kind}) on {DescribePlainEnglish(d.Date)} with {DescribePlainEnglish(d.Offset)}",
        _ => expr.ToString() ?? "unknown"
    };

    private static string OpName(ComparisonKind kind) => kind switch {
        ComparisonKind.Equal => "==",
        ComparisonKind.NotEqual => "!=",
        ComparisonKind.LessThan => "<",
        ComparisonKind.LessThanOrEqual => "<=",
        ComparisonKind.GreaterThan => ">",
        ComparisonKind.GreaterThanOrEqual => ">=",
        _ => kind.ToString()
    };

    private static string OpEnglish(ComparisonKind kind) => kind switch {
        ComparisonKind.Equal => "equals",
        ComparisonKind.NotEqual => "does not equal",
        ComparisonKind.LessThan => "is less than",
        ComparisonKind.LessThanOrEqual => "is at most",
        ComparisonKind.GreaterThan => "is greater than",
        ComparisonKind.GreaterThanOrEqual => "is at least",
        _ => kind.ToString()
    };

    private static string FormatLiteral(object? value) => value switch {
        null => "null",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? "null"
    };

    // ── V0.6: export_domain_to_csharp ────────────────────────────
    [McpServerTool(Name = "export_domain_to_csharp"), Description("Generates C# source code for an entire domain session as a set of record/class definitions. Each entity becomes a C# record with its properties, navigation properties (as collections for many, references for one), stages as enums or additional state, and actions as methods with their lowered effects as the method body. Useful for inspecting how a domain model maps to C#.")]
    public static DomainToolResponse ExportDomainToCSharp(
        [Description("Session ID")] string sessionId) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' not found.", Affordances: ["create_domain_session", "list_sessions"]);
        if (state.LatestAnalysis is null)
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' has no analysis. Apply a domain first (apply_dsl or evolution).", SessionId: sessionId, Affordances: ["apply_dsl", "get_domain_overview"]);

        try {
            var files = state.Modeling.Emit(state.Domain, state.LatestAnalysis);
            var csharp = string.Join("\n\n", files.Select(f => f.Source));
            return new DomainToolResponse(Success: true, Message: $"Domain exported to C#: {files.Count} file(s).", SessionId: sessionId, Data: new { csharp, fileCount = files.Count, files = files.Select(f => f.FileName).ToArray() }, Affordances: ["get_domain_overview", "get_entity_detail", "apply_dsl"]);
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"Domain-to-C# export failed: {ex.Message}", SessionId: sessionId, Data: new { error = ex.Message }, Affordances: []);
        }
    }

    // ── DTO types ───────────────────────────────────────────────

    internal sealed record DescribeExpressionData(
        [property: JsonPropertyName("structured")] string Structured,
        [property: JsonPropertyName("plainEnglish")] string PlainEnglish
    );

    internal sealed record DomainElementData(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("parentEntity")] string? ParentEntity,
        [property: JsonPropertyName("detail")] string Detail,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("expression")] DescribeExpressionData? Expression = null
    );

    // ── V0.3: describe_domain_element ───────────────────────────

    [McpServerTool(Name = "describe_domain_element"), Description("Returns a structured description of a domain element (entity, stage, action, policy, or relationship) from the session domain. For stage/action/policy, specify entityName if the name is not unique across entities.")]
    public static DomainToolResponse DescribeDomainElement(
        [Description("Session ID")] string sessionId,
        [Description("Element kind: 'entity', 'stage', 'action', 'policy', or 'relationship'")] string kind,
        [Description("Element name")] string name,
        [Description("Optional entity name to disambiguate stage/action/policy (recommended when multiple entities share the same element name)")] string? entityName = null) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' not found.", Affordances: ["create_domain_session", "list_sessions"]);

        try {
            return kind.ToLowerInvariant() switch {
                "entity" => DescribeEntity(sessionId, state, name),
                "stage" => DescribeStage(sessionId, state, name, entityName),
                "action" => DescribeAction(sessionId, state, name, entityName),
                "policy" => DescribePolicy(sessionId, state, name, entityName),
                "relationship" => DescribeRelationship(sessionId, state, name, entityName),
                _ => new DomainToolResponse(Success: false, Message: $"Unknown element kind '{kind}'. Use: entity, stage, action, policy, or relationship.", SessionId: sessionId, Affordances: ["get_entity_detail", "get_domain_overview"])
            };
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"Failed to describe element: {ex.Message}", SessionId: sessionId, Affordances: ["get_entity_detail", "get_domain_overview"]);
        }
    }

    private static DomainToolResponse DescribeEntity(string sessionId, McpSessionState state, string name) {
        var detail = DomainQueries.GetEntity(state.Domain, name, state.LatestAnalysis);
        if (detail is null) return new DomainToolResponse(Success: false, Message: $"Entity '{name}' not found.", SessionId: sessionId, Affordances: ["get_domain_overview", "add"]);

        var sb = new StringBuilder();
        sb.Append($"Entity '{name}' with {detail.Properties.Count} properties, {detail.Stages.Count} stages, {detail.Actions.Count} actions, {detail.Policies.Count} policies.");

        if (detail.Navigations.Count > 0) sb.Append($" Navigations: {string.Join(", ", detail.Navigations.Select(n => n.RelationshipName))}.");

        return new DomainToolResponse(Success: true, Message: sb.ToString(), SessionId: sessionId, Data: new DomainElementData("entity", name, null, sb.ToString(), sb.ToString()), Affordances: ["get_entity_detail", "get_domain_analysis"]);
    }

    private static DomainToolResponse DescribeStage(string sessionId, McpSessionState state, string name, string? entityName = null) {
        if (state.LatestAnalysis is null)
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' has no analysis. Apply a domain first (apply_dsl or evolution).", SessionId: sessionId, Affordances: ["apply_dsl", "get_domain_overview"]);

        // Structural entity enumeration (projection) + ESM-backed stage resolution only.
        // Missing EntityStructureMetadata ≠ not-found.
        var analysis = state.LatestAnalysis;
        var entities = entityName is not null
            ? state.Domain.Types.OfType<Entity>().Where(e => string.Equals(e.Name, entityName, StringComparison.Ordinal))
            : state.Domain.Types.OfType<Entity>();
        var missingStructure = false;
        foreach (var entity in entities) {
            if (analysis.GetStructure(entity) is null) {
                missingStructure = true;
                continue;
            }
            if (!analysis.TryGetStage(entity, name, out var stage) || stage is null)
                continue;

            var effectiveActionCount = analysis.GetEffectiveActions(state.Domain, entity, name).Count;
            var effectivePolicyCount = analysis.GetEffectivePolicies(state.Domain, entity, name).Count;

            var sb = new StringBuilder();
            sb.Append($"Stage '{name}' on entity '{entity.Name}' with {effectiveActionCount} effective actions, {effectivePolicyCount} effective policies, {stage.Subscriptions.Count} subscriptions.");
            if (stage.OnEntryEffects.Count > 0) sb.Append($" Has {stage.OnEntryEffects.Count} entry effect(s).");
            if (stage.OnExitEffects.Count > 0) sb.Append($" Has {stage.OnExitEffects.Count} exit effect(s).");
            if (effectiveActionCount > stage.Actions.Count)
                sb.Append($" Includes {effectiveActionCount - stage.Actions.Count} inherited action(s).");
            return new DomainToolResponse(Success: true, Message: sb.ToString(), SessionId: sessionId, Data: new DomainElementData("stage", name, entity.Name, sb.ToString(), sb.ToString()), Affordances: ["get_entity_detail"]);
        }

        if (missingStructure)
            return new DomainToolResponse(Success: false, Message: $"Session analysis is missing EntityStructureMetadata required to describe stage '{name}'.", SessionId: sessionId, Affordances: ["get_domain_analysis"]);

        return new DomainToolResponse(Success: false, Message: $"Stage '{name}' not found on any entity.", SessionId: sessionId, Affordances: ["get_domain_overview"]);
    }

    private static DomainToolResponse DescribeAction(string sessionId, McpSessionState state, string name, string? entityName = null) {
        if (state.LatestAnalysis is null)
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' has no analysis. Apply a domain first (apply_dsl or evolution).", SessionId: sessionId, Affordances: ["apply_dsl", "get_domain_overview"]);

        // Catalog action map only. Missing catalog ≠ not-found.
        var analysis = state.LatestAnalysis;
        if (analysis.GetCatalog(state.Domain) is null)
            return new DomainToolResponse(Success: false, Message: $"Session analysis is missing DomainCatalogMetadata required to describe action '{name}'.", SessionId: sessionId, Affordances: ["get_domain_analysis"]);

        var entities = entityName is not null
            ? state.Domain.Types.OfType<Entity>().Where(e => string.Equals(e.Name, entityName, StringComparison.Ordinal))
            : state.Domain.Types.OfType<Entity>();
        foreach (var entity in entities) {
            // Entity-first search priority: this is a describe/search tool, not
            // runtime dispatch. TryResolveAction uses stage-first + SA semantics
            // for execution; entity-first is preferred here for broad search.
            var arm = analysis.GetActionResolution(state.Domain, entity);
            Poly.DomainModeling.Ontology.Action? action = null;
            if (arm is not null) {
                arm.EntityActions.TryGetValue(name, out action);
                if (action is null) {
                    foreach (var kv in arm.StageActions) {
                        if (kv.Value.TryGetValue(name, out var sa)) {
                            action = sa;
                            break;
                        }
                    }
                }
            }
            if (action is null) continue;

            var actionCap = analysis.GetMetadata<ActionCapabilityMetadata>(action);
            var transitionTargets = actionCap?.View.TransitionTargets.Select(t => t.Name).ToList() ?? [];

            var sb = new StringBuilder();
            sb.Append($"Action '{name}' on entity '{entity.Name}'");
            if (action.Parameters.Count > 0) sb.Append($" with parameters: {string.Join(", ", action.Parameters.Select(p => $"{p.Name}: {p.Type.TypeName}"))}");
            sb.Append($", {action.Effects.Count} effect(s), {action.Policies.Count} guard(s).");
            if (transitionTargets.Count > 0)
                sb.Append($" Transitions to: {string.Join(", ", transitionTargets)}.");
            return new DomainToolResponse(Success: true, Message: sb.ToString(), SessionId: sessionId, Data: new DomainElementData("action", name, entity.Name, sb.ToString(), sb.ToString()), Affordances: ["get_entity_detail"]);
        }

        return new DomainToolResponse(Success: false, Message: $"Action '{name}' not found on any entity.", SessionId: sessionId, Affordances: ["get_entity_detail"]);
    }

    private static DomainToolResponse DescribePolicy(string sessionId, McpSessionState state, string name, string? entityName = null) {
        if (state.LatestAnalysis is null)
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' has no analysis. Apply a domain first (apply_dsl or evolution).", SessionId: sessionId, Affordances: ["apply_dsl", "get_domain_overview"]);

        // Catalog required (analysis published). Policies are Domain facts.
        var analysis = state.LatestAnalysis;
        if (analysis.GetCatalog(state.Domain) is null)
            return new DomainToolResponse(Success: false, Message: $"Session analysis is missing DomainCatalogMetadata required to describe policy '{name}'.", SessionId: sessionId, Affordances: ["get_domain_analysis"]);

        var entities = entityName is not null
            ? state.Domain.Types.OfType<Entity>().Where(e => string.Equals(e.Name, entityName, StringComparison.Ordinal))
            : state.Domain.Types.OfType<Entity>();
        foreach (var entity in entities) {
            Policy? policy = null;
            string? scope = null;

            policy = entity.Policies.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
            if (policy is not null)
                scope = "entity";

            if (policy is null) {
                foreach (var stage in entity.Stages) {
                    var sp = stage.Policies.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
                    if (sp is not null) {
                        policy = sp;
                        scope = $"stage '{stage.Name}'";
                        break;
                    }
                }
            }

            if (policy is null) {
                foreach (var action in entity.Actions.Concat(entity.Stages.SelectMany(s => s.Actions))) {
                    var ap = action.Policies.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
                    if (ap is not null) {
                        policy = ap;
                        scope = $"action '{action.Name}'";
                        break;
                    }
                }
            }

            if (policy is null) continue;

            var exprDescription = DescribeExpression(policy.Expression);
            var sb = new StringBuilder();
            sb.Append($"Policy '{name}' on entity '{entity.Name}'");
            if (scope is not null) sb.Append($" ({scope} scope)");
            sb.Append($": {exprDescription.PlainEnglish}.");
            return new DomainToolResponse(Success: true, Message: sb.ToString(), SessionId: sessionId, Data: new DomainElementData("policy", name, entity.Name, sb.ToString(), sb.ToString(), exprDescription), Affordances: ["get_policy_expression", "get_entity_detail"]);
        }

        return new DomainToolResponse(Success: false, Message: $"Policy '{name}' not found on any entity.", SessionId: sessionId, Affordances: ["get_entity_detail"]);
    }

    private static DomainToolResponse DescribeRelationship(string sessionId, McpSessionState state, string name, string? sourceEntityName = null) {
        if (state.LatestAnalysis is null)
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' has no analysis. Apply a domain first (apply_dsl or evolution).", SessionId: sessionId, Affordances: ["apply_dsl", "get_domain_overview"]);

        // Catalog relationship lookup only. Missing catalog ≠ not-found.
        var analysis = state.LatestAnalysis;
        var relLookup = analysis.GetRelationshipLookup(state.Domain);
        if (relLookup is null)
            return new DomainToolResponse(Success: false, Message: $"Session analysis is missing DomainCatalogMetadata (relationship lookup) required to describe relationship '{name}'.", SessionId: sessionId, Affordances: ["get_domain_analysis"]);

        // Relationship identity is (source entity, name). entityName disambiguates a
        // name declared on multiple source entities; otherwise the name must be unique.
        Relationship? rel = null;
        if (sourceEntityName is not null) {
            relLookup.TryGetRelationship(sourceEntityName, name, out rel);
        }
        else {
            var sources = relLookup.BySourceEntity
                .Where(kv => kv.Value.ContainsKey(name))
                .Select(kv => kv.Key)
                .ToList();
            if (sources.Count > 1)
                return new DomainToolResponse(Success: false, Message: $"Relationship '{name}' exists on multiple source entities ({string.Join(", ", sources)}). Provide entityName to disambiguate.", SessionId: sessionId, Affordances: ["get_relationships", "get_domain_analysis"]);
            if (sources.Count == 1)
                relLookup.TryGetRelationship(sources[0], name, out rel);
        }

        if (rel is null)
            return new DomainToolResponse(Success: false, Message: $"Relationship '{name}' not found.", SessionId: sessionId, Affordances: ["get_domain_overview", "add"]);

        var cardinality = rel.Cardinality switch { RelationshipCardinality.OneToOne => "one-to-one", RelationshipCardinality.OneToMany => "one-to-many", RelationshipCardinality.ManyToMany => "many-to-many", _ => rel.Cardinality.ToString() };
        var sb = new StringBuilder();
        sb.Append($"Relationship '{name}': {rel.Source.TypeName} → {rel.Target.TypeName} ({cardinality})");
        if (rel.SourceOwnsTarget) sb.Append(", source owns target");
        if (rel.Stages.Count > 0) sb.Append($", {rel.Stages.Count} stage(s)");
        return new DomainToolResponse(Success: true, Message: sb.ToString(), SessionId: sessionId, Data: new DomainElementData("relationship", name, null, sb.ToString(), sb.ToString()), Affordances: ["get_entity_detail", "get_domain_overview"]);
    }

    // ── oracle_expression (fragment probe, not named-policy simulate)

    [McpServerTool(Name = "oracle_expression"), Description("Authoring probe: VM-evaluates a DSL expression fragment against a local property bag. Not named-policy evaluate and not invoke_action. No DomainSession; types are inferred onto a synthetic Entity(\"Subject\"). Returns {'result': true/false}. Related/nav expressions fail closed. For a named policy, create_instance then evaluate_policy.")]
    public static DomainToolResponse OracleExpression(
        [Description("DSL expression fragment, e.g. `Age >= 18`.")] string expression,
        [Description("JSON object of property values, e.g. \"{\\\"Age\\\":25,\\\"Status\\\":\\\"Active\\\"}\"")] string propertiesJson) {
        // Parse expression
        var parseFailure = TryParseDslExpression(expression, out var expr);
        if (parseFailure is not null) return parseFailure;

        // Parse subject properties
        Dictionary<string, object?> subjectValues;
        try {
            subjectValues = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(propertiesJson)
                ?.ToDictionary(kv => kv.Key, kv => JsonElementToClrValue(kv.Value), StringComparer.Ordinal)
                ?? throw new ArgumentException("Failed to parse properties JSON.");
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"Invalid subject properties: {ex.Message}", Affordances: []);
        }

        if (subjectValues.Count == 0) {
            return new DomainToolResponse(Success: false, Message: "Subject properties must not be empty.", Affordances: []);
        }

        // G1: Fail closed — validate all property references exist in the subject bag.
        var missingProperties = CollectPropertyNames(expr)
            .Where(p => !subjectValues.ContainsKey(p))
            .ToList();
        if (missingProperties.Count > 0) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Expression references properties not present in the subject properties bag: [{string.Join(", ", missingProperties)}]. " +
                         "Provide values for these properties in the propertiesJson parameter.",
                Affordances: ["add", "create_instance", "evaluate_policy"]);
        }

        try {
            // Infer property types from the expression to ensure VM compatibility
            var propertyTypes = InferPropertyTypes(expr);
            var props = subjectValues.Keys
                .Select(k => new Property(k, new DomainTypeReference(
                    propertyTypes.TryGetValue(k, out var t) ? t : "Text"), []))
                .ToList();
            var entity = new Entity("Subject", props, [], [], []);
            var policy = new Policy("_sim", expr);
            var domain = new Domain("Subject", [entity]) {
                Extensions = [.. ExtensionCatalog.ProductAuthoring],
            };
            var instance = DomainEntityInstance.Create(entity, subjectValues, domain);
            var result = instance.EvaluatePolicy(policy);

            var data = new { result };
            return new DomainToolResponse(Success: true, Message: result ? "Expression passed (true)." : "Expression failed (false).", Data: data, Affordances: ["add", "create_instance", "evaluate_policy"]);
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"Oracle failed: {ex.Message}", Data: new { error = ex.Message }, Affordances: []);
        }
    }

    /// <summary>Collects all <see cref="PropertyAccess"/> names from an expression tree.</summary>
    private static HashSet<string> CollectPropertyNames(DomainExpression expr) {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Walk(expr);
        return names;

        void Walk(DomainExpression e) {
            switch (e) {
                case PropertyAccess pa:
                    names.Add(pa.Name);
                    break;
                case OwnedAccess oa:
                    Walk(oa.Inner);
                    break;
                case Exists ex:
                    Walk(ex.Target);
                    break;
                case NotExists nex:
                    Walk(nex.Target);
                    break;
                case Subtract sub:
                    Walk(sub.Left); Walk(sub.Right);
                    break;
                case Add add:
                    Walk(add.Left); Walk(add.Right);
                    break;
                case Multiply mul:
                    Walk(mul.Left); Walk(mul.Right);
                    break;
                case Divide div:
                    Walk(div.Left); Walk(div.Right);
                    break;
                case DateOperation dOp:
                    Walk(dOp.Date); Walk(dOp.Offset);
                    break;
                case RelationshipNavigation rn:
                    Walk(rn.TargetProperty);
                    break;
                case AnyExpr any:
                    Walk(any.Body);
                    break;
                case AllExpr all:
                    Walk(all.Body);
                    break;
                case NoneExpr none:
                    Walk(none.Body);
                    break;
                case CountExpr cnt:
                    if (cnt.Body is not null) Walk(cnt.Body);
                    break;
                case Comparison cmp:
                    Walk(cmp.Left); Walk(cmp.Right);
                    break;
                case And and:
                    Walk(and.Left); Walk(and.Right);
                    break;
                case Or or:
                    Walk(or.Left); Walk(or.Right);
                    break;
                case Not not:
                    Walk(not.Operand);
                    break;
                    // ParameterAccess, Literal — no property references
            }
        }
    }

    /// <summary>
    /// Walks a <see cref="DomainExpression"/> to infer property types from comparison literal values.
    /// Number literals → "Number", boolean literals → "Boolean", else → "Text".
    /// </summary>
    private static Dictionary<string, string> InferPropertyTypes(DomainExpression expr) {
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        InferTypesCore(expr, types);
        return types;
    }

    private static void InferTypesCore(DomainExpression expr, Dictionary<string, string> types) {
        switch (expr) {
            case Comparison c:
                var leftProp = c.Left is PropertyAccess lp ? lp.Name : null;
                var rightLit = c.Right is Literal rl ? rl.Value : null;
                var rightProp = c.Right is PropertyAccess rp ? rp.Name : null;
                var leftLit = c.Left is Literal ll ? ll.Value : null;

                if (leftProp is not null && rightLit is not null)
                    types[leftProp] = InferClrType(rightLit);
                if (rightProp is not null && leftLit is not null)
                    types[rightProp] = InferClrType(leftLit);
                InferTypesCore(c.Left, types);
                InferTypesCore(c.Right, types);
                break;
            case And a:
                InferTypesCore(a.Left, types);
                InferTypesCore(a.Right, types);
                break;
            case Or o:
                InferTypesCore(o.Left, types);
                InferTypesCore(o.Right, types);
                break;
            case Not n:
                InferTypesCore(n.Operand, types);
                break;
            case Add a:
                InferTypesCore(a.Left, types);
                InferTypesCore(a.Right, types);
                break;
            case Subtract s:
                InferTypesCore(s.Left, types);
                InferTypesCore(s.Right, types);
                break;
            case Multiply m:
                InferTypesCore(m.Left, types);
                InferTypesCore(m.Right, types);
                break;
            case Divide d:
                InferTypesCore(d.Left, types);
                InferTypesCore(d.Right, types);
                break;
            case Exists e:
                InferTypesCore(e.Target, types);
                break;
            case NotExists ne:
                InferTypesCore(ne.Target, types);
                break;
            case OwnedAccess oa:
                InferTypesCore(oa.Inner, types);
                break;
            case RelationshipNavigation rn:
                InferTypesCore(rn.TargetProperty, types);
                break;
        }
    }

    private static string InferClrType(object? value) => value switch {
        long or int or double or decimal or float => "Number",
        bool => "Boolean",
        _ => "Text"
    };

    private static object? JsonElementToClrValue(JsonElement je) => je.ValueKind switch {
        JsonValueKind.Number when je.TryGetInt32(out var i) => (long)i,
        JsonValueKind.Number when je.TryGetInt64(out var l) => l,
        JsonValueKind.Number => (long)je.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => je.GetString(),
        JsonValueKind.Null => null,
        _ => je.GetRawText()
    };
}