using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol.Server;

using Poly.DomainModeling.Ontology;
using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

using Entity = Poly.DomainModeling.Ontology.Entity;

namespace Poly.Mcp.Surface;

/// <summary>
/// The v2 agent surface: seven outcome tools over the same domain engine. The framing is
/// "describe the domain you are working in, prove it, then produce the artifact or
/// simulation the task needs" — not session administration or one tool per endpoint.
/// </summary>
[McpServerToolType]
internal sealed class PolySurfaceTools(ISessionStore store, ILogger<PolySurfaceTools> logger) {
    private readonly DslTool _dsl = new(store, NullLogger<DslTool>.Instance);
    private readonly QueryTool _query = new(store, NullLogger<QueryTool>.Instance);
    private readonly OracleTool _oracle = new(store, NullLogger<OracleTool>.Instance);
    private readonly RuntimeTool _runtime = new(store, NullLogger<RuntimeTool>.Instance);
    private readonly PolicyTool _policy = new(store, NullLogger<PolicyTool>.Instance);
    private readonly SessionTool _session = new(store, NullLogger<SessionTool>.Instance);

    private const int FindingCap = 20;
    private const int InlineLimit = 4000;

    // ── 1. guide ────────────────────────────────────────────────

    [McpServerTool(Name = "poly_guide", Title = "Learn the Poly DSL", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PolyOutputSchema)), Description("Learn the .poly surface: the minimal document, the author→prove→export workflow, the unsupported constructs, and a section index. Omit topic for a short quickstart; pass a section number or title (or \"all\") for depth. Start here on the first Poly task.")]
    public PolyResponse Guide(
        [Description("Optional guide section number or title; omit for the quickstart.")] string? topic = null) {
        if (string.IsNullOrWhiteSpace(topic)) {
            return new PolyResponse(
                Ok: true,
                Summary: "Poly quickstart.",
                Findings: [],
                Data: new { guide = Quickstart },
                Next: ["poly_open", "poly_define", "poly_check"],
                State: null);
        }

        var response = _dsl.GetDslGuide(topic);
        return new PolyResponse(
            Ok: response.Success,
            Summary: response.Message,
            Findings: [],
            Data: response.Data,
            Next: response.Success ? ["poly_define", "poly_check"] : ["poly_guide"],
            State: null);
    }

    // ── 2. open ─────────────────────────────────────────────────

    [McpServerTool(Name = "poly_open", Title = "Open a Poly workspace", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PolyOutputSchema)), Description("Creates, lists, or closes a Poly workspace. A workspace is the handle every stateful call needs — keep one per task. op=create (default) returns the handle and state; op=list returns the current workspaces; op=close with workspace removes it when the task is done. An expired or unknown handle returns WORKSPACE_REQUIRED, never an empty model.")]
    public PolyResponse Open(
        [Description("create (default), list, or close")] string? op = null,
        [Description("Optional model name for create, e.g. \"Returns\".")] string? name = null,
        [Description("Workspace handle to close when op=close.")] string? workspace = null) {
        var action = string.IsNullOrWhiteSpace(op) ? "create" : op.Trim().ToLowerInvariant();
        if (action == "list") {
            var workspaces = store.ListSessions()
                .Select(StateOf)
                .Where(state => state is not null)
                .ToList();
            return new PolyResponse(
                Ok: true,
                Summary: $"{workspaces.Count} workspace(s) in this process.",
                Findings: [],
                Data: new { sessions = workspaces },
                Next: ["poly_open"],
                State: null);
        }

        if (action == "close") {
            if (string.IsNullOrWhiteSpace(workspace) || !store.TryGet(workspace, out _)) {
                return new PolyResponse(
                    Ok: false,
                    Summary: "Workspace not found; nothing to close. Pass the handle returned by poly_open.",
                    Findings: [],
                    Data: null,
                    Next: ["poly_open"],
                    State: null,
                    ErrorCode: "WORKSPACE_NOT_FOUND");
            }
            store.Remove(workspace);
            return new PolyResponse(
                Ok: true,
                Summary: "Workspace closed. Its model and instances are gone.",
                Findings: [],
                Data: null,
                Next: ["poly_open"],
                State: null);
        }

        if (action != "create") {
            return new PolyResponse(
                Ok: false,
                Summary: $"Unknown op '{op}'. Use create, list, or close.",
                Findings: [],
                Data: null,
                Next: ["poly_open"],
                State: null,
                ErrorCode: "UNKNOWN_OP");
        }

        var created = _session.CreateDomainSession(string.IsNullOrWhiteSpace(name) ? "Domain" : name);
        return new PolyResponse(
            Ok: created.Success,
            Summary: created.Message,
            Findings: [],
            Data: created.Data,
            Next: created.Success && created.SessionId is { } handle
                ? Next(handle, "poly_define", "poly_guide")
                : ["poly_open"],
            State: created.SessionId is null ? null : StateOf(created.SessionId));
    }

    // ── 3. define ───────────────────────────────────────────────

    [McpServerTool(Name = "poly_define", Title = "Define the domain", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PolyOutputSchema)), Description("Validates and applies one .poly document to the workspace. This is the only authoring path: write the domain down, let the constraint-aware analysis gate it, and fix what it reports. apply=false is a dry run that changes nothing. A rejected document changes nothing, returns structured findings (severity, code, element path, message) and leaves the revision unchanged.")]
    public PolyResponse Define(
        [Description("Workspace handle from poly_open.")] string workspace,
        [Description("The complete .poly document. It replaces the current model; it does not merge.")] string poly,
        [Description("Apply the document when valid (default true). false is a dry run.")] bool apply = true) {
        if (!TryWorkspace(workspace, out _))
            return WorkspaceRequired();

        logger.LogDebug("poly_define {Workspace} apply={Apply}", workspace, apply);
        var response = apply
            ? _dsl.ApplyDsl(workspace, poly)
            : _dsl.ValidateDsl(workspace, poly);

        var findings = FindingsFrom(response);
        return new PolyResponse(
            Ok: response.Success,
            Summary: apply ? response.Message : $"Dry run: {response.Message}",
            Findings: findings,
            Data: response.Data is DslValidationData validation
                ? new {
                    validation.DomainName,
                    validation.Valid,
                    validation.EntityCount,
                    validation.RelationshipCount,
                    validation.ErrorCount,
                    validation.WarningCount,
                    validation.HintCount,
                }
                : response.Data,
            Next: response.Success
                ? (apply
                    ? Next(workspace, "poly_check", "poly_export")
                    : Next(workspace, "poly_define", "poly_check"))
                : Next(workspace, "poly_guide", "poly_define"),
            State: StateOf(workspace));
    }

    // ── 4. check ────────────────────────────────────────────────

    [McpServerTool(Name = "poly_check", Title = "Prove the model", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PolyOutputSchema)), Description("Proves a fact about the current model: the whole-model verdict, or one selected action/property, or an expression against a binding bag. Returns proven, violated, or unknown. Findings carry the element path and the analyzer's message. Does not change the model. Use poly_define to fix a violation.")]
    public PolyResponse Check(
        [Description("Workspace handle from poly_open.")] string workspace,
        [Description("Optional element path to focus, e.g. \"Product\" or \"Product.Submit\".")] string? select = null,
        [Description("Optional JSON object of action arguments; argument-specific proofs are not implemented yet and return unknown.")] string? args = null,
        [Description("Optional DSL expression fragment to evaluate, e.g. \"Total > 100\". Requires bindings.")] string? expression = null,
        [Description("Optional JSON object of property values for expression.")] string? bindings = null) {
        if (!TryWorkspace(workspace, out var workspaceState))
            return WorkspaceRequired();

        if (!string.IsNullOrWhiteSpace(expression)) {
            var evaluated = _oracle.OracleExpression(
                expression, string.IsNullOrWhiteSpace(bindings) ? "{}" : bindings);
            var result = evaluated.Success
                ? JsonSerializer.SerializeToNode(evaluated.Data)?["result"]?.GetValue<bool>()
                : null;
            var expressionVerdict = !evaluated.Success ? "unknown" : result == true ? "proven" : "violated";
            var expressionFindings = evaluated.Success ? [] : FindingsFrom(evaluated);
            if (!evaluated.Success && expressionFindings.Count == 0)
                expressionFindings = [new PolyFinding("Error", null, "", evaluated.Message)];
            return new PolyResponse(
                Ok: evaluated.Success,
                Summary: evaluated.Success
                    ? $"Expression {expressionVerdict}: {evaluated.Message}"
                    : $"Unknown: {evaluated.Message}",
                Findings: expressionFindings,
                Data: new { verdict = expressionVerdict, result },
                Next: evaluated.Success
                    ? Next(workspace, "poly_check", "poly_simulate")
                    : Next(workspace, "poly_check"),
                State: StateOf(workspace));
        }

        var analysis = _query.GetDomainAnalysis(workspace);
        if (!analysis.Success) {
            return new PolyResponse(
                Ok: false,
                Summary: analysis.Message,
                Findings: FindingsFrom(analysis),
                Data: analysis.Data,
                Next: Next(workspace, "poly_define", "poly_check"),
                State: StateOf(workspace));
        }

        var allFindings = AllFindings(analysis);
        var selectMatched = true;
        if (!string.IsNullOrWhiteSpace(select)) {
            var normalized = NormalizeSelect(select, workspaceState.Domain.Name);
            var focused = allFindings.Where(f => PathMatches(f.Path, normalized)).ToList();
            selectMatched = focused.Count > 0 || ElementExists(workspaceState.Domain, normalized);
            allFindings = focused;
        }

        var hintCount = allFindings.Count(f => string.Equals(f.Severity, "Hint", StringComparison.OrdinalIgnoreCase));
        var reportable = allFindings
            .Where(f => !string.Equals(f.Severity, "Hint", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var errorCount = reportable.Count(f => string.Equals(f.Severity, "Error", StringComparison.OrdinalIgnoreCase));
        var warningCount = reportable.Count(f => string.Equals(f.Severity, "Warning", StringComparison.OrdinalIgnoreCase));
        var verdict = !selectMatched
            ? "unknown"
            : !string.IsNullOrWhiteSpace(args)
                ? "unknown"
                : errorCount > 0 ? "violated" : "proven";

        var summary = verdict switch {
            "violated" => $"Violated: {errorCount} error(s), {warningCount} warning(s), {hintCount} hint(s).",
            "unknown" when !selectMatched => $"Unknown: no element or finding matches '{select}'.",
            "unknown" => "Unknown: argument-specific proofs are not implemented yet; use poly_simulate to execute the action.",
            _ => $"Proven: no errors, {warningCount} warning(s), {hintCount} hint(s).",
        };

        return new PolyResponse(
            Ok: true,
            Summary: summary,
            Findings: reportable.Take(FindingCap).ToList(),
            Data: new {
                verdict,
                select,
                errorCount,
                warningCount,
                hintCount,
                findingsTotal = reportable.Count,
                truncated = reportable.Count > FindingCap,
            },
            Next: verdict == "violated"
                ? Next(workspace, "poly_guide", "poly_define")
                : Next(workspace, "poly_export", "poly_simulate"),
            State: StateOf(workspace));
    }

    // ── 5. inspect ──────────────────────────────────────────────

    [McpServerTool(Name = "poly_inspect", Title = "Inspect the model", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PolyOutputSchema)), Description("Reads one part of the model: omit select for an overview, pass an entity name for that entity, or pass \"Entity.Member\" for one action, stage, or policy. Brief by default returns the one-line summary; detail=\"full\" returns the full element. Read-only.")]
    public PolyResponse Inspect(
        [Description("Workspace handle from poly_open.")] string workspace,
        [Description("Optional \"Entity\" or \"Entity.Member\" path.")] string? select = null,
        [Description("brief (default) or full.")] string? detail = null) {
        if (!TryWorkspace(workspace, out var state))
            return WorkspaceRequired();

        DomainToolResponse response;
        if (string.IsNullOrWhiteSpace(select)) {
            response = _query.GetDomainOverview(workspace);
        }
        else {
            var parts = select.Split('.', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 1) {
                response = _query.GetEntityDetail(workspace, parts[0]);
            }
            else {
                var entity = state.Domain.Types.OfType<Entity>()
                    .FirstOrDefault(e => string.Equals(e.Name, parts[0], StringComparison.Ordinal));
                var kind = entity is not null
                    && entity.Stages.Any(s => string.Equals(s.Name, parts[1], StringComparison.Ordinal))
                        ? "stage"
                        : entity is not null
                            && entity.Policies.Any(p => string.Equals(p.Name, parts[1], StringComparison.Ordinal))
                                ? "policy"
                                : "action";
                response = _oracle.DescribeDomainElement(workspace, kind, parts[1], parts[0]);
            }
        }

        var brief = !string.Equals(detail, "full", StringComparison.OrdinalIgnoreCase);
        var data = string.IsNullOrWhiteSpace(select) || !brief
            ? response.Data
            : new { description = response.Message };
        return new PolyResponse(
            Ok: response.Success,
            Summary: response.Message,
            Findings: FindingsFrom(response),
            Data: data,
            Next: response.Success ? Next(workspace, "poly_inspect", "poly_check") : Next(workspace, "poly_check"),
            State: StateOf(workspace));
    }

    // ── 6. export ───────────────────────────────────────────────

    [McpServerTool(Name = "poly_export", Title = "Export an artifact", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PolyOutputSchema)), Description("Produces the artifact the task needs from the current model: dsl, json-schema, csharp, markdown, html, json-ld, or shacl. Small artifacts are returned inline; anything over 4 KB is written to a file and returned as {path, bytes, sha256}. Read-only; the model is unchanged.")]
    public PolyResponse Export(
        [Description("Workspace handle from poly_open.")] string workspace,
        [Description("dsl | json-schema | csharp | markdown | html | json-ld | shacl")] string format,
        [Description("Return the whole artifact inline even when larger than 4 KB (default false).")] bool inline = false) {
        if (!TryWorkspace(workspace, out _))
            return WorkspaceRequired();

        var normalized = format.Trim().ToLowerInvariant();
        var (response, property, extension) = normalized switch {
            "dsl" => (_dsl.ExportDsl(workspace), "poly", "poly"),
            "json-schema" => (_oracle.ExportDomainToJsonSchema(workspace), "json", "json"),
            "csharp" => (_oracle.ExportDomainToCSharp(workspace), "csharp", "cs"),
            "markdown" => (_query.GetDomainReport(workspace), "markdown", "md"),
            "html" => (_oracle.ExportDomainToHtml(workspace), "html", "html"),
            "json-ld" => (_oracle.ExportDomainToJsonLd(workspace), "jsonLd", "jsonld"),
            "shacl" => (_oracle.ExportDomainToShacl(workspace), "shapes", "jsonld"),
            _ => (new DomainToolResponse(false, $"Unknown format '{format}'. Use: dsl, json-schema, csharp, markdown, html, json-ld, shacl."), "", ""),
        };

        if (!response.Success)
            return new PolyResponse(false, response.Message, FindingsFrom(response), response.Data, Next(workspace, "poly_export"), StateOf(workspace));

        var content = ContentOf(response, property);
        if (string.IsNullOrWhiteSpace(content)) {
            return new PolyResponse(
                Ok: false,
                Summary: $"Export '{normalized}' produced no content; nothing was returned.",
                Findings: [new PolyFinding("Error", null, "", $"The '{normalized}' exporter returned no '{property}' payload.")],
                Data: null,
                Next: Next(workspace, "poly_export"),
                State: StateOf(workspace));
        }

        var bytes = Encoding.UTF8.GetByteCount(content);
        object data;
        if (!inline && bytes > InlineLimit) {
            var name = $"poly-{Sanitize(workspace)}-{normalized}-" +
                $"{Path.GetFileNameWithoutExtension(Path.GetRandomFileName())}.{extension}";
            var path = Path.Combine(Path.GetTempPath(), name);
            File.WriteAllText(path, content);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            data = new { format = normalized, path, bytes, sha256 = hash };
        }
        else {
            data = new { format = normalized, content, bytes };
        }

        return new PolyResponse(
            Ok: true,
            Summary: $"Exported {normalized} ({bytes} bytes).",
            Findings: [],
            Data: data,
            Next: Next(workspace, "poly_inspect", "poly_check"),
            State: StateOf(workspace));
    }

    // ── 7. simulate ─────────────────────────────────────────────

    [McpServerTool(Name = "poly_simulate", Title = "Run a scenario", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PolyOutputSchema)), Description("Runs an ordered scenario against the model and returns one trace entry per step. Steps are a JSON array of {op: create|link|unlink|invoke|evaluate|get|list, ...}. A guard-blocked step is a failed step, not a failed call; earlier steps stay committed. reset=true starts from a clean instance state.")]
    public PolyResponse Simulate(
        [Description("Workspace handle from poly_open.")] string workspace,
        [Description("JSON array of steps, e.g. [{\"op\":\"create\",\"entity\":\"Product\",\"properties\":{\"Total\":42}}]")] string steps,
        [Description("Clear existing instances before running (default false).")] bool reset = false,
        [Description("Stop at the first failed step (default true).")] bool stopOnError = true) {
        if (!TryWorkspace(workspace, out var state))
            return WorkspaceRequired();

        JsonDocument document;
        try {
            document = JsonDocument.Parse(steps);
        }
        catch (JsonException ex) {
            return new PolyResponse(false, $"Invalid steps JSON: {ex.Message}", [], null, ["poly_simulate"], StateOf(workspace));
        }

        using var _ = document;
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return new PolyResponse(false, "steps must be a JSON array.", [], null, ["poly_simulate"], StateOf(workspace));

        // Only mutate after the scenario is known to be well-formed; a rejected call
        // must not clear instances or bump the revision.
        if (reset) {
            state = ResetInstances(workspace, state);
        }

        var trace = new List<object>();
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        int? failedAt = null;
        var index = 0;
        foreach (var step in document.RootElement.EnumerateArray()) {
            DomainToolResponse response;
            var op = "";
            try {
                if (step.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("Each step must be a JSON object.");
                op = step.TryGetProperty("op", out var opValue) ? opValue.GetString() ?? "" : "";
                response = op switch {
                    "create" => _runtime.CreateInstance(
                        workspace,
                        Required(step, "entity"),
                        OptionalJson(step, "properties")),
                    "link" => _runtime.LinkInstances(
                        workspace,
                        Resolve(Required(step, "sourceInstanceId"), aliases),
                        Required(step, "relationship"),
                        Resolve(Required(step, "targetInstanceId"), aliases)),
                    "unlink" => _runtime.UnlinkInstances(
                        workspace,
                        Resolve(Required(step, "sourceInstanceId"), aliases),
                        Required(step, "relationship"),
                        Resolve(Required(step, "targetInstanceId"), aliases)),
                    "invoke" => _runtime.InvokeAction(
                        workspace,
                        Resolve(Required(step, "instanceId"), aliases),
                        Required(step, "action"),
                        OptionalJson(step, "args")),
                    "evaluate" => _policy.EvaluatePolicy(
                        workspace,
                        Required(step, "entity"),
                        Required(step, "policy"),
                        Resolve(Required(step, "instanceId"), aliases)),
                    "get" => _runtime.GetInstance(
                        workspace, Resolve(Required(step, "instanceId"), aliases)),
                    "list" => _runtime.ListInstances(
                        workspace,
                        step.TryGetProperty("entity", out var entityValue) ? entityValue.GetString() : null),
                    _ => new DomainToolResponse(false, $"Unknown step op '{op}'. Use create, link, unlink, invoke, evaluate, get, or list."),
                };

                if (response.Success
                    && op == "create"
                    && step.TryGetProperty("as", out var alias)
                    && alias.GetString() is { Length: > 0 } aliasName
                    && InstanceIdOf(response) is { } createdId) {
                    aliases[aliasName] = createdId;
                }
            }
            catch (Exception ex) {
                response = new DomainToolResponse(false, $"Step {index} failed: {ex.Message}");
            }

            trace.Add(new { index, op, ok = response.Success, summary = response.Message, data = response.Data });
            if (!response.Success) {
                failedAt ??= index;
                if (stopOnError)
                    break;
            }
            index++;
        }

        return new PolyResponse(
            Ok: true,
            Summary: failedAt is null
                ? $"Scenario ran: {trace.Count} step(s), all succeeded."
                : $"Scenario ran: {trace.Count} step(s), failed at {failedAt}.",
            Findings: [],
            Data: new { trace, count = trace.Count, failedAt },
            Next: Next(workspace, "poly_check", "poly_inspect"),
            State: StateOf(workspace));
    }

    // ── helpers ─────────────────────────────────────────────────

    private bool TryWorkspace(string workspace, out McpSessionState state) {
        if (!string.IsNullOrWhiteSpace(workspace) && store.TryGet(workspace, out state))
            return true;
        state = null!;
        return false;
    }

    private static PolyResponse WorkspaceRequired() =>
        new(
            Ok: false,
            Summary: "A workspace handle is required. Call poly_open first, then pass the returned handle as workspace.",
            Findings: [],
            Data: null,
            Next: ["poly_open"],
            State: null,
            ErrorCode: "WORKSPACE_REQUIRED");

    private PolyState? StateOf(string workspace) {
        if (string.IsNullOrWhiteSpace(workspace) || !store.TryGet(workspace, out var state))
            return null;
        return new PolyState(
            Workspace: workspace,
            Model: state.Domain.Name,
            Revision: state.Revision,
            Entities: state.Domain.Types.OfType<Entity>().Count(),
            Instances: state.ExecutorIfCreated?.Instances.Count() ?? 0);
    }

    private McpSessionState ResetInstances(string workspace, McpSessionState state) {
        // Clear the instance set in place: the lowered module and the session that lowered
        // it stay bound. Replacing the state re-binds the same domain to a second session
        // and the cache refuses to evict the module, which broke the next create.
        state.ResetInstances();
        return state;
    }

    private static List<PolyFinding> AllFindings(DomainToolResponse response) {
        if (response.Data is DslValidationData validation)
            return validation.Findings
                .Select(f => new PolyFinding(f.Severity, f.Code, f.ElementPath, f.Message))
                .ToList();

        if (response.Data is AnalysisData analysis && analysis.DiagnosticFacts is { Count: > 0 } facts)
            return facts
                .Select(f => new PolyFinding(f.Severity, f.Code, f.ElementPath, f.Message))
                .DistinctBy(f => (f.Code, f.Path, f.Message))
                .ToList();

        if (response.Diagnostics is { Count: > 0 } diagnostics)
            return diagnostics
                .Select(message => new PolyFinding("Error", null, "", message))
                .ToList();

        return [];
    }

    private static List<PolyFinding> FindingsFrom(DomainToolResponse response) =>
        AllFindings(response).Take(FindingCap).ToList();

    private static IReadOnlyList<string> Next(string workspace, params string[] tools) =>
        tools.Select(tool => $"{tool}(workspace:\"{workspace}\")").ToList();

    /// <summary>
    /// Analysis paths are <c>Domain/Entity/Member</c>; callers may write
    /// <c>Entity.Member</c> or the domain-qualified slash form. Normalize to
    /// <c>Entity/Member</c> so a documented selector cannot silently match nothing.
    /// </summary>
    private static string NormalizeSelect(string select, string domain) {
        var parts = select.Split(['.', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 1 && string.Equals(parts[0], domain, StringComparison.Ordinal))
            parts = parts[1..];
        return string.Join('/', parts);
    }

    private static bool PathMatches(string path, string normalized) =>
        path.Equals(normalized, StringComparison.Ordinal)
        || path.EndsWith("/" + normalized, StringComparison.Ordinal)
        || path.StartsWith(normalized + "/", StringComparison.Ordinal)
        || path.Contains("/" + normalized + "/", StringComparison.Ordinal);

    private static bool ElementExists(Domain domain, string normalized) {
        var parts = normalized.Split('/');
        var entity = domain.Types.OfType<Entity>()
            .FirstOrDefault(e => string.Equals(e.Name, parts[0], StringComparison.Ordinal));
        if (entity is null)
            return false;
        if (parts.Length == 1)
            return true;

        var member = parts[1];
        return entity.Properties.Any(p => string.Equals(p.Name, member, StringComparison.Ordinal))
            || entity.Actions.Any(a => string.Equals(a.Name, member, StringComparison.Ordinal))
            || entity.Policies.Any(p => string.Equals(p.Name, member, StringComparison.Ordinal))
            || entity.Stages.Any(s => string.Equals(s.Name, member, StringComparison.Ordinal)
                || s.Actions.Any(a => string.Equals(a.Name, member, StringComparison.Ordinal)));
    }

    private static string? ContentOf(DomainToolResponse response, string property) {
        if (string.IsNullOrWhiteSpace(property))
            return null;
        var node = JsonSerializer.SerializeToNode(response.Data);
        return node?[property]?.GetValue<string>();
    }

    private static string Required(JsonElement step, string name) =>
        step.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? throw new InvalidOperationException($"Step is missing '{name}'.")
            : throw new InvalidOperationException($"Step is missing string '{name}'.");

    private static string Resolve(string instanceId, IReadOnlyDictionary<string, string> aliases) =>
        aliases.TryGetValue(instanceId, out var resolved) ? resolved : instanceId;

    private static string? InstanceIdOf(DomainToolResponse response) {
        var node = JsonSerializer.SerializeToNode(response.Data);
        return node?["instance"]?["instanceId"]?.GetValue<string>();
    }

    private static string? OptionalJson(JsonElement step, string name) =>
        step.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? value.GetRawText()
            : null;

    private static string Sanitize(string value) =>
        string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

    private const string Quickstart = """
        Poly models a domain as one executable .poly document: entities, typed properties
        with constraints, stages, actions, and policies. Write the domain down, let the
        analyzer prove it, then export or simulate it.

        Workflow: poly_guide -> poly_open -> poly_define(poly) -> poly_check -> poly_export or poly_simulate.

        domain Shop
        Product: entity {
          Sku: Text unique
          Price: Number range(0, 10000)
          Draft: stage { Publish: action { transition to Live } }
          Live: stage {}
        }

        Rules: properties are required unless `optional`; navigations are inline
        (`lines: many Line`); guards use `require Policy`; a first-stage
        `entry { assign Field to value }` initializes a field. Ask poly_guide for a
        section (e.g. "3") for depth.
        """;
}