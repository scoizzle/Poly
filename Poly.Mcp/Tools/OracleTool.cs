using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Server;

using Poly.Analysis;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology.Bootstrap;
using Poly.DomainModeling.Queries;
using Poly.Interpretation.CSharp;
using Poly.Mcp.Sessions;

using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Mcp.Tools;

/// <summary>
/// Oracle tools: visibility, lowering, description, and a DSL-fragment probe.
/// These tools are read-only — they never mutate session state. Named-policy
/// and named-action simulate is <c>evaluate_policy</c> / <c>invoke_action</c>.
/// </summary>
internal sealed class OracleTool(ISessionStore store, ILogger<OracleTool> logger) {
    // ── Shared helpers ──────────────────────────────────────────

    private static DomainToolResponse? TryParseDslExpression(string expressionDsl, out DomainExpression expr) {
        expr = null!;
        try {
            // Product authoring tables (temporal + storage) — same folds as add(kind: policy).
            expr = DslExpressionFragment.Resolve(
                DslExpressionFragment.ParseExpressionFragment(expressionDsl), ExtensionCatalog.Core.Authoring);
            return null;
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"Invalid expression: {ex.Message}", Data: new { parseError = ex.Message }, Affordances: []);
        }
    }

    private static List<string> ErrorMessages(AnalysisResult analysis) =>
        [.. analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)];

    // ── V0.6: export_domain_to_csharp ────────────────────────────
    public DomainToolResponse ExportDomainToCSharp(
        [Description("Session ID")] string sessionId) {
        if (!store.TryGet(sessionId, out var state))
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' not found.", Affordances: ["create_domain_session", "list_sessions"]);
        logger.LogDebug("export_domain_to_csharp {SessionId}", sessionId);
        if (state.LatestAnalysis is null)
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' has no analysis. Apply a domain first (apply_dsl or evolution).", SessionId: sessionId, Affordances: ["apply_dsl", "get_domain_overview"]);
        if (state.LatestAnalysis.HasErrors)
            return new DomainToolResponse(Success: false, Message: "Domain has analysis errors; nothing was exported.", SessionId: sessionId, Diagnostics: ErrorMessages(state.LatestAnalysis), Affordances: ["get_domain_analysis"]);

        (DomainSession Session, AnalysisResult Analysis)? execution = null;
        try {
            // Emit through the compilation the executor runs, not the authored session: the
            // constraint-compiled module is the one consumers get, and binding a second session
            // here would evict it from RuntimeAnalysisCache (#387).
            execution = state.Execution;
            // The shared produce gate (the one polyc build runs), on the compilation the executor
            // runs. A refusal is a diagnostic, not an exception.
            var produced = Poly.Compile.PolyCompiler.Produce(
                execution.Value.Session, state.Domain, execution.Value.Analysis, "<workspace>");
            if (!produced.Succeeded) {
                var reasons = produced.Diagnostics.Where(d => d.IsError).Select(d => d.Message).ToList();
                return new DomainToolResponse(Success: false, Message: $"Domain-to-C# export failed: {(reasons.Count > 0 ? reasons[0] : "the domain was refused")}", SessionId: sessionId, Data: new { error = reasons.FirstOrDefault() }, Diagnostics: reasons.Take(3).ToList() is { Count: > 0 } top ? top : null, Affordances: []);
            }
            var files = produced.Files;
            // One compilation unit, not the per-entity files joined: `poly_export csharp` returns a single
            // .cs artifact, and concatenated units put a `using` after a type and a second file-scoped
            // namespace in it (CS1529/CS8954, #489).
            var csharp = execution.Value.Session.EmitSingleUnit(state.Domain, execution.Value.Analysis);
            return new DomainToolResponse(Success: true, Message: $"Domain exported to C#: {files.Count} file(s).", SessionId: sessionId, Data: new { csharp, fileCount = files.Count, files = files.Select(f => f.FileName).ToArray() }, Affordances: ["get_domain_overview", "get_entity_detail", "apply_dsl"]);
        }
        catch (Exception ex) {
            // The execution analysis can carry errors the authoring analysis does not (the
            // persistence overlay adds analyzers), and lowering refuses the domain for them. Report
            // them rather than the bare message.
            var diagnostics = state.ExecutionAnalysisIfCreated?.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)
                .Take(3)
                .ToList();
            return new DomainToolResponse(Success: false, Message: $"Domain-to-C# export failed: {ex.Message}", SessionId: sessionId, Data: new { error = ex.Message }, Diagnostics: diagnostics is { Count: > 0 } ? diagnostics : null, Affordances: []);
        }
    }

    // ── JSON Schema export ───────────────────────────────────────
    public DomainToolResponse ExportDomainToJsonSchema(
        [Description("Session ID")] string sessionId) {
        if (!store.TryGet(sessionId, out var state))
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' not found.", Affordances: ["create_domain_session", "list_sessions"]);
        logger.LogDebug("export_domain_to_json_schema {SessionId}", sessionId);
        if (state.LatestAnalysis is null)
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' has no analysis. Apply a domain first (apply_dsl or evolution).", SessionId: sessionId, Affordances: ["apply_dsl", "validate_dsl", "get_domain_overview"]);
        if (state.LatestAnalysis.HasErrors)
            return new DomainToolResponse(Success: false, Message: "Domain has analysis errors; nothing was exported.", SessionId: sessionId, Diagnostics: ErrorMessages(state.LatestAnalysis), Affordances: ["get_domain_analysis", "validate_dsl"]);

        try {
            var json = JsonSchemaExporter.Export(state.Domain);
            var entityCount = state.Domain.Types.OfType<Entity>().Count();
            return new DomainToolResponse(
                Success: true,
                Message: $"Domain exported as JSON Schema: {entityCount} entities.",
                SessionId: sessionId,
                Revision: state.Revision,
                Data: new JsonSchemaExportData(json, entityCount),
                Affordances: ["get_domain_overview", "export_dsl", "export_domain_to_csharp"]);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"JSON Schema export failed: {ex.Message}",
                SessionId: sessionId,
                Revision: state.Revision,
                Data: new { error = ex.Message },
                Affordances: ["get_domain_analysis"]);
        }
    }

    // ── JSON-LD export ───────────────────────────────────────────
    public DomainToolResponse ExportDomainToJsonLd(
        [Description("Session ID")] string sessionId) {
        if (!store.TryGet(sessionId, out var state))
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' not found.", Affordances: ["create_domain_session", "list_sessions"]);
        logger.LogDebug("export_domain_to_json_ld {SessionId}", sessionId);
        if (state.LatestAnalysis is null)
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' has no analysis. Apply a domain first (apply_dsl or evolution).", SessionId: sessionId, Affordances: ["apply_dsl", "validate_dsl", "get_domain_overview"]);
        if (state.LatestAnalysis.HasErrors)
            return new DomainToolResponse(Success: false, Message: "Domain has analysis errors; nothing was exported.", SessionId: sessionId, Diagnostics: ErrorMessages(state.LatestAnalysis), Affordances: ["get_domain_analysis", "validate_dsl"]);

        try {
            var jsonLd = JsonLdExporter.Export(state.Domain);
            var entities = state.Domain.Types.OfType<Entity>().ToList();
            var valueTypes = state.Domain.Types.OfType<Poly.DomainModeling.Ontology.ValueType>().ToList();
            var classCount = entities.Count + valueTypes.Count
                + state.Domain.Types.OfType<EnumType>().Count();
            var propertyCount = entities.Sum(e => e.Properties.Count + e.Navigations.Count)
                + valueTypes.Sum(v => v.Properties.Count);
            return new DomainToolResponse(
                Success: true,
                Message: $"Domain exported as JSON-LD: {classCount} classes, {propertyCount} properties.",
                SessionId: sessionId,
                Revision: state.Revision,
                Data: new JsonLdExportData(jsonLd, classCount, propertyCount),
                Affordances: ["get_domain_overview", "export_domain_to_json_schema", "export_dsl"]);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"JSON-LD export failed: {ex.Message}",
                SessionId: sessionId,
                Revision: state.Revision,
                Data: new { error = ex.Message },
                Affordances: ["get_domain_analysis"]);
        }
    }

    // ── SHACL export ─────────────────────────────────────────────
    public DomainToolResponse ExportDomainToShacl(
        [Description("Session ID")] string sessionId) {
        if (!store.TryGet(sessionId, out var state))
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' not found.", Affordances: ["create_domain_session", "list_sessions"]);
        logger.LogDebug("export_domain_to_shacl {SessionId}", sessionId);
        if (state.LatestAnalysis is null)
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' has no analysis. Apply a domain first (apply_dsl or evolution).", SessionId: sessionId, Affordances: ["apply_dsl", "validate_dsl", "get_domain_overview"]);
        if (state.LatestAnalysis.HasErrors)
            return new DomainToolResponse(Success: false, Message: "Domain has analysis errors; nothing was exported.", SessionId: sessionId, Diagnostics: ErrorMessages(state.LatestAnalysis), Affordances: ["get_domain_analysis", "validate_dsl"]);

        try {
            var shapes = ShaclExporter.Export(state.Domain);
            var entities = state.Domain.Types.OfType<Entity>().ToList();
            var propertyCount = entities.Sum(e => e.Properties.Count + e.Navigations.Count);
            return new DomainToolResponse(
                Success: true,
                Message: $"Domain exported as SHACL: {entities.Count} node shapes, {propertyCount} property shapes.",
                SessionId: sessionId,
                Revision: state.Revision,
                Data: new ShaclExportData(shapes, entities.Count, propertyCount),
                Affordances: ["get_domain_overview", "export_domain_to_json_ld", "export_domain_to_json_schema"]);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"SHACL export failed: {ex.Message}",
                SessionId: sessionId,
                Revision: state.Revision,
                Data: new { error = ex.Message },
                Affordances: ["get_domain_analysis"]);
        }
    }

    // ── HTML docs export ─────────────────────────────────────────
    public DomainToolResponse ExportDomainToHtml(
        [Description("Session ID")] string sessionId) {
        if (!store.TryGet(sessionId, out var state))
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' not found.", Affordances: ["create_domain_session", "list_sessions"]);
        logger.LogDebug("export_domain_to_html {SessionId}", sessionId);
        if (state.LatestAnalysis is null)
            return new DomainToolResponse(Success: false, Message: $"Session '{sessionId}' has no analysis. Apply a domain first (apply_dsl or evolution).", SessionId: sessionId, Affordances: ["apply_dsl", "validate_dsl", "get_domain_overview"]);
        if (state.LatestAnalysis.HasErrors)
            return new DomainToolResponse(Success: false, Message: "Domain has analysis errors; nothing was exported.", SessionId: sessionId, Diagnostics: ErrorMessages(state.LatestAnalysis), Affordances: ["get_domain_analysis", "validate_dsl"]);

        try {
            var html = DomainHtmlExporter.Export(state.Domain);
            var entityCount = state.Domain.Types.OfType<Entity>().Count();
            return new DomainToolResponse(
                Success: true,
                Message: $"Domain exported as HTML documentation: {entityCount} entities.",
                SessionId: sessionId,
                Revision: state.Revision,
                Data: new HtmlExportData(html, entityCount),
                Affordances: ["get_domain_report", "export_domain_to_json_schema", "export_dsl"]);
        }
        catch (Exception ex) {
            return new DomainToolResponse(
                Success: false,
                Message: $"HTML export failed: {ex.Message}",
                SessionId: sessionId,
                Revision: state.Revision,
                Data: new { error = ex.Message },
                Affordances: ["get_domain_analysis"]);
        }
    }

    // ── DTO types ───────────────────────────────────────────────

    internal sealed record HtmlExportData(
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("entityCount")] int EntityCount);

    internal sealed record ShaclExportData(
        [property: JsonPropertyName("shapes")] string Shapes,
        [property: JsonPropertyName("nodeShapeCount")] int NodeShapeCount,
        [property: JsonPropertyName("propertyShapeCount")] int PropertyShapeCount);

    internal sealed record JsonLdExportData(
        [property: JsonPropertyName("jsonLd")] string JsonLd,
        [property: JsonPropertyName("classCount")] int ClassCount,
        [property: JsonPropertyName("propertyCount")] int PropertyCount);

    internal sealed record JsonSchemaExportData(
        [property: JsonPropertyName("json")] string Json,
        [property: JsonPropertyName("entityCount")] int EntityCount);


    // ── oracle_expression (fragment probe, not named-policy simulate)

    public DomainToolResponse OracleExpression(
        [Description("DSL expression fragment, e.g. `Age >= 18`.")] string expression,
        [Description("JSON object of property values, e.g. \"{\\\"Age\\\":25,\\\"Status\\\":\\\"Active\\\"}\". Empty for an expression that reads no property.")] string bindings,
        [Description("The model the fragment is probed against: its functions and enums are in scope. Omit for the bare primitives.")] Domain? model = null) {
        // Parse expression
        var parseFailure = TryParseDslExpression(expression, out var expr);
        if (parseFailure is not null) return parseFailure;

        // Parse the binding bag
        Dictionary<string, object?> subjectValues;
        try {
            subjectValues = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bindings)
                ?.ToDictionary(kv => kv.Key, kv => JsonElementToClrValue(kv.Value), StringComparer.Ordinal)
                ?? throw new ArgumentException("Failed to parse bindings JSON.");
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"Invalid bindings: {ex.Message}", Affordances: []);
        }

        // G1: Fail closed — every name the expression reads is a bound property. A bare enum
        // member (`Level is Senior`) is not a property; the analysis resolves it against the
        // binding's declared enum type, or refuses it there.
        var missingProperties = CollectPropertyNames(expr)
            .Where(p => !subjectValues.ContainsKey(p) && !IsEnumMemberName(model, p))
            .ToList();
        if (missingProperties.Count > 0) {
            return new DomainToolResponse(
                Success: false,
                Message: $"Expression references properties not present in the bindings bag: [{string.Join(", ", missingProperties)}]. " +
                         "Provide a value for each in the bindings argument.",
                Affordances: ["poly_check"]);
        }

        try {
            // The declared type when the model states one (an enum property must stay its enum,
            // so `Level is Senior` resolves); else the literal's own type.
            var propertyTypes = PropertyTypes(model, expr, subjectValues.Keys);
            var props = subjectValues.Keys
                .Select(k => new Property(k, new DomainTypeReference(propertyTypes[k]), []))
                .ToList();
            var policy = new Policy("_sim", expr);
            var entity = new Entity("Subject", props, [], [policy], []); // Policies = [policy] so EvaluatePolicy cache hits
            // The model's own functions and enums are in scope for the fragment; the primitives
            // a parsed domain starts with resolve its property types.
            var domain = ProbeDomain(model, entity);
            // Create refuses a domain with analysis errors; report them as diagnostics instead.
            // Create reads this same cached analysis.
            var analysis = RuntimeAnalysisCache.Analysis(domain);
            if (analysis.HasErrors) {
                // The summary carries the first errors: a bare "has analysis errors" leaves the
                // caller to go reading findings to learn what to write.
                var errors = ErrorMessages(analysis);
                return new DomainToolResponse(
                    Success: false,
                    Message: $"Expression has analysis errors: {string.Join(" ", errors.Take(3))}",
                    Diagnostics: errors,
                    Affordances: []);
            }
            var executor = new ModuleInstanceExecutor(domain);
            var created = executor.Create(entity.Name, subjectValues);
            if (!created.Succeeded || created.Instance is not { } instance)
                return new DomainToolResponse(Success: false, Message: $"Oracle failed: {created.Error}", Data: new { error = created.Error }, Affordances: []);
            var result = executor.EvaluatePolicy(instance, policy.Name);

            var data = new { result };
            return new DomainToolResponse(Success: true, Message: result ? "Expression passed (true)." : "Expression failed (false).", Data: data, Affordances: ["add", "create_instance", "evaluate_policy"]);
        }
        catch (Exception ex) {
            return new DomainToolResponse(Success: false, Message: $"Oracle failed: {ex.Message}", Data: new { error = ex.Message }, Affordances: []);
        }
    }

    /// <summary>
    /// The domain the fragment is probed against: the primitives, the model's own enum and value
    /// types, its functions, and one synthetic <c>Subject</c> entity carrying the binding bag.
    /// Entities are not carried: a probe has one subject, and the model's own instances,
    /// actions and constraints are not what it asks about.
    /// </summary>
    private static Domain ProbeDomain(Domain? model, Entity subject) {
        var boot = DomainFactory.Create("Subject");
        var carried = model?.Types.Where(t => t is EnumType or ValueType).ToList() ?? [];
        return boot with {
            Types = [.. boot.Types, .. carried, subject],
            Functions = model?.Functions ?? [],
        };
    }

    /// <summary>
    /// A bound property's type: the model's declaration when the model states exactly one type
    /// for that name (so an enum property stays its enum, and a call argument keeps its type),
    /// else the type the comparison literals imply. A name the model declares with two types is
    /// not one answer, so it falls back rather than pick one.
    /// </summary>
    private static Dictionary<string, string> PropertyTypes(
        Domain? model, DomainExpression expr, IEnumerable<string> bound) {
        var inferred = InferPropertyTypes(expr);
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in bound)
            types[name] = DeclaredTypeName(model, name)
                ?? (inferred.TryGetValue(name, out var t) ? t : "Text");
        return types;
    }

    private static string? DeclaredTypeName(Domain? model, string name) {
        if (model is null)
            return null;
        var declared = model.Types
            .SelectMany(t => t.Properties)
            .Where(p => string.Equals(p.Name, name, StringComparison.Ordinal))
            .Select(p => p.Type.TypeName)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return declared.Count == 1 ? declared[0] : null;
    }

    /// <summary>A name one of the model's enums declares: a bare enum member, not a property.</summary>
    private static bool IsEnumMemberName(Domain? model, string name) =>
        model is not null
        && model.Types.OfType<EnumType>()
            .Any(e => e.MemberNames.Contains(name, StringComparer.Ordinal));

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