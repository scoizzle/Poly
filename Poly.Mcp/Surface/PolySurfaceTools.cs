using System.Collections.Concurrent;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol.Server;

using Poly.Compile;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Language;
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

    /// <summary>
    /// The artifact files each workspace has written: <c>poly-&lt;workspace&gt;-&lt;format&gt;-&lt;sha16&gt;.&lt;ext&gt;</c> in
    /// the OS temp directory, content-addressed so an unchanged export reuses one file. Closing the
    /// workspace removes them; without that a long-lived session grows the temp directory per call (#598).
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _exportedFiles =
        new(StringComparer.Ordinal);

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
                Next: ["poly_workspace", "poly_define", "poly_check"],
                State: null);
        }

        var response = _dsl.GetDslGuide(topic);
        return new PolyResponse(
            Ok: response.Success,
            Summary: response.Message,
            Findings: [],
            Data: WithoutVerifyMarkers(response.Data),
            Next: response.Success ? ["poly_define", "poly_check"] : ["poly_guide"],
            State: null);
    }

    // The guide file carries test-harness markers (<!-- poly-verify: ... -->) above its fences.
    // Every topic this tool serves passes here once; the markers stay in the file for the tests.
    private static readonly Regex VerifyMarkerLine = new(@"^[ \t]*<!--\s*poly-verify:.*?-->[ \t]*\r?\n", RegexOptions.Multiline | RegexOptions.Compiled);

    internal static string StripVerifyMarkers(string guide) => VerifyMarkerLine.Replace(guide, "");

    private static object? WithoutVerifyMarkers(object? data) {
        if (data is null)
            return null;
        var node = JsonSerializer.SerializeToNode(data)!.AsObject();
        if (node["guide"]?.GetValue<string>() is { } guide)
            node["guide"] = StripVerifyMarkers(guide);
        return node;
    }

    // ── 2. open ─────────────────────────────────────────────────

    [McpServerTool(Name = "poly_workspace", Title = "Manage the Poly workspace", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PolyOutputSchema)), Description("Creates, lists, or closes a Poly workspace. A workspace is the handle every stateful call needs — keep one per task. op=create (default) returns the handle and state; op=list returns the current workspaces; op=close with workspace removes it when the task is done. An expired or unknown handle returns WORKSPACE_REQUIRED, never an empty model.")]
    public PolyResponse Workspace(
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
                Next: ["poly_workspace"],
                State: null);
        }

        if (action == "close") {
            if (string.IsNullOrWhiteSpace(workspace) || !store.TryGet(workspace, out _)) {
                return new PolyResponse(
                    Ok: false,
                    Summary: "Workspace not found; nothing to close. Pass the handle returned by poly_workspace.",
                    Findings: [],
                    Data: null,
                    Next: ["poly_workspace"],
                    State: null,
                    ErrorCode: "WORKSPACE_NOT_FOUND");
            }
            store.Remove(workspace);
            RemoveExportedFiles(workspace);
            return new PolyResponse(
                Ok: true,
                Summary: "Workspace closed. Its model, instances and exported artifact files are gone.",
                Findings: [],
                Data: null,
                Next: ["poly_workspace"],
                State: null);
        }

        if (action != "create") {
            return new PolyResponse(
                Ok: false,
                Summary: $"Unknown op '{op}'. Use create, list, or close.",
                Findings: [],
                Data: null,
                Next: ["poly_workspace"],
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
                : ["poly_workspace"],
            State: created.SessionId is null ? null : StateOf(created.SessionId));
    }

    // ── 3. define ───────────────────────────────────────────────

    [McpServerTool(Name = "poly_define", Title = "Define the domain", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PolyOutputSchema)), Description("Validates and applies one .poly document to the workspace, or edits the document last applied. This is the only authoring path: write the domain down, let the constraint-aware analysis gate it, and fix what it reports. Pass poly to replace the model with a complete document, or edit to change the stored document in place. apply=false is a dry run that changes nothing. A rejected document or edit changes nothing, returns structured findings (severity, code, element path, message) and leaves the revision unchanged. A successful apply replaces the lowered module, so the workspace's simulated instances are cleared: the summary and data.instancesCleared say how many.")]
    public PolyResponse Define(
        [Description("Workspace handle from poly_workspace.")] string workspace,
        [Description("The complete .poly document. It replaces the current model; it does not merge. Pass exactly one of poly or edit.")] string? poly = null,
        [Description("Apply the document when valid (default true). false is a dry run.")] bool apply = true,
        [Description("A JSON array of ops that edit the stored document and keep the rest of it, comments and order included: [{\"op\":\"add\",\"parent\":\"Product\",\"dsl\":\"Amount: Decimal\"},{\"op\":\"remove\",\"path\":\"Product.Total\"},{\"op\":\"replace\",\"path\":\"Product.Note\",\"dsl\":\"Note: Text optional\"}]. add takes parent (\"\" for top level) and dsl; remove takes path (including a uses line as \"uses:<pack>\"); replace takes path and dsl. dsl must be one declaration (a member, a top-level declaration or a uses line) with balanced braces (EDIT_BAD_SNIPPET otherwise). Needs a document applied by poly_define first.")] string? edit = null) {
        if (!TryWorkspace(workspace, out var current))
            return WorkspaceRequired();

        if ((poly is null) == (edit is null))
            return DefineRejected(workspace, "EDIT_ARGUMENT_CONFLICT",
                "Pass exactly one of poly (the complete document) or edit (a JSON array of ops).");

        logger.LogDebug("poly_define {Workspace} apply={Apply} edit={Edit}", workspace, apply, edit is not null);
        var instancesBefore = current.ExecutorIfCreated?.Instances.Count() ?? 0;
        if (edit is null) {
            var replaced = apply
                ? _dsl.ApplyDsl(workspace, poly!)
                : _dsl.ValidateDsl(workspace, poly!);
            var cleared = apply && replaced.Success ? instancesBefore : 0;
            return DefineResponse(workspace, apply, replaced, WithInstancesCleared(ValidationData(replaced), cleared),
                ClearedSummary(replaced.Message, cleared));
        }

        if (current.SourceText is null)
            return DefineRejected(workspace, "EDIT_NO_SOURCE",
                "There is no applied document to edit. Send the complete document with poly first; edit only works on a document poly_define applied.");

        var outcome = PolyEdit.Apply(current.SourceText, edit);
        if (!outcome.Ok)
            return DefineRejected(workspace, outcome.ErrorCode!, outcome.Error!);

        var response = apply
            ? _dsl.ApplyDslAtRevision(workspace, outcome.Text!, current.Revision)
            : _dsl.ValidateDsl(workspace, outcome.Text!);

        if (!response.Success && apply && store.TryGet(workspace, out var after) && after.Revision != current.Revision)
            return DefineRejected(workspace, "EDIT_STALE",
                "The model changed while this edit was being applied; nothing was written. Re-read it and send the edit again.");

        var data = new Dictionary<string, object?>();
        if (ValidationData(response) is { } validation)
            foreach (var property in JsonSerializer.SerializeToElement(validation).EnumerateObject())
                data[property.Name] = property.Value.Clone();
        data["applied"] = outcome.Applied;
        data["spliced"] = outcome.Text!.Length <= InlineLimit ? outcome.Text : null;

        var summary = response.Message;
        if (!response.Success && FailedLine(response.Message) is { } line) {
            data["failedLine"] = line;
            if (outcome.Regions.FirstOrDefault(r => line >= r.StartLine && line <= r.EndLine) is { } region)
                summary = $"{summary} (in edit {region.Index})";
        }
        var clearedInstances = apply && response.Success ? instancesBefore : 0;
        if (clearedInstances > 0) {
            summary = ClearedSummary(summary, clearedInstances)!;
            data["instancesCleared"] = clearedInstances;
        }

        return DefineResponse(workspace, apply, response, data, summary);
    }

    /// <summary>The applied summary plus how many simulated instances the apply discarded (#575).</summary>
    private static string? ClearedSummary(string? summary, int cleared) =>
        cleared > 0
            ? $"{summary} {cleared} simulated instance{(cleared == 1 ? "" : "s")} cleared."
            : summary;

    /// <summary>Adds <c>instancesCleared</c> to the applied result's own payload; nothing was cleared, nothing is added.</summary>
    private static object? WithInstancesCleared(object? data, int cleared) {
        if (data is null || cleared == 0)
            return data;
        var node = JsonSerializer.SerializeToNode(data)!.AsObject();
        node["instancesCleared"] = cleared;
        return node;
    }

    private static int? FailedLine(string message) {
        var match = System.Text.RegularExpressions.Regex.Match(message, @"line (\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    private PolyResponse DefineRejected(string workspace, string errorCode, string summary) =>
        new(
            Ok: false,
            Summary: summary,
            Findings: [],
            Data: null,
            Next: Next(workspace, "poly_guide", "poly_define"),
            State: StateOf(workspace),
            ErrorCode: errorCode);

    private static object? ValidationData(DomainToolResponse response) =>
        response.Data is DslValidationData validation
            ? new {
                domainName = validation.DomainName,
                valid = validation.Valid,
                entityCount = validation.EntityCount,
                relationshipCount = validation.RelationshipCount,
                errorCount = validation.ErrorCount,
                warningCount = validation.WarningCount,
                hintCount = validation.HintCount,
            }
            : response.Data;

    private PolyResponse DefineResponse(
        string workspace, bool apply, DomainToolResponse response, object? data, string? summary = null) =>
        new(
            Ok: response.Success,
            Summary: apply ? summary ?? response.Message : $"Dry run: {summary ?? response.Message}",
            Findings: FindingsFrom(response),
            Data: data,
            Next: response.Success
                ? (apply
                    ? Next(workspace, "poly_check", "poly_export")
                    : Next(workspace, "poly_define", "poly_check"))
                : Next(workspace, "poly_guide", "poly_define"),
            State: StateOf(workspace));

    // ── 4. check ────────────────────────────────────────────────

    [McpServerTool(Name = "poly_check", Title = "Prove the model", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PolyOutputSchema)), Description("Proves a fact about the current model: the whole-model verdict, or one selected action/property, or an expression against a binding bag. An expression is probed against the model's own functions and enums; every property it reads needs a value in bindings, and an expression that reads none needs no bindings. Returns proven, violated, or unknown: proven only when every write was verified against its range, length, pattern or equals constraints (unique, required and enum membership are checked elsewhere), unknown (with the count of unverified findings) while any write could not be: bound the source, widen the target, or guard a non-parameter write; a Text or Number action parameter written into a bounded property stays unknown until parameter constraints exist. Findings carry the element path and the analyzer's message. Does not change the model. Use poly_define to fix a violation.")]
    public PolyResponse Check(
        [Description("Workspace handle from poly_workspace.")] string workspace,
        [Description("Optional element path to focus, e.g. \"Product\" or \"Product.Submit\".")] string? select = null,
        [Description("Optional JSON object of action arguments; argument-specific proofs are not implemented yet and return unknown.")] string? args = null,
        [Description("Optional DSL expression fragment to evaluate, e.g. \"Total > 100\". The model's functions and enums are in scope. Each property it reads needs a value in bindings; an expression that reads none needs no bindings.")] string? expression = null,
        [Description("Optional JSON object of property values for expression, keyed by property name, e.g. \"{\\\"Total\\\":150}\".")] string? bindings = null) {
        if (!TryWorkspace(workspace, out var workspaceState))
            return WorkspaceRequired();

        if (!string.IsNullOrWhiteSpace(expression)) {
            var evaluated = _oracle.OracleExpression(
                expression, string.IsNullOrWhiteSpace(bindings) ? "{}" : bindings, workspaceState.Domain);
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
        string? scopePath = null;
        IReadOnlyList<ActionInvariantFact>? witness = null;
        if (!string.IsNullOrWhiteSpace(select)) {
            var normalized = NormalizeSelect(select, workspaceState.Domain.Name);
            scopePath = normalized;
            var selectParts = normalized.Split('/');
            if (selectParts.Length == 2 && analysis.Data is AnalysisData { ActionInvariants: { } invariants }) {
                var contexts = invariants
                    .Where(i => string.Equals(i.EntityName, selectParts[0], StringComparison.Ordinal)
                        && string.Equals(i.ActionName, selectParts[1], StringComparison.Ordinal))
                    .ToList();
                witness = contexts.Count > 0 ? contexts : null;
            }
            var focused = allFindings.Where(f => PathMatches(f.Path, normalized)).ToList();
            selectMatched = focused.Count > 0 || ElementExists(workspaceState.Domain, normalized);
            allFindings = focused;

            // #534: a typo used to answer ok with an "unknown" verdict, indistinguishable from a
            // legitimately undecided proof. The resolver, not a second guess, says whether the path names anything.
            if (!selectMatched) {
                var inspected = InspectService.Inspect(workspace, workspaceState, select, _query);
                if (!inspected.Ok)
                    return new PolyResponse(
                        Ok: false,
                        Summary: inspected.Summary,
                        Findings: [],
                        Data: inspected.Data,
                        Next: Next(workspace, "poly_inspect", "poly_check"),
                        State: StateOf(workspace),
                        ErrorCode: inspected.ErrorCode ?? InspectService.ElementNotFoundCode);
            }
        }

        var hintCount = allFindings.Count(f => string.Equals(f.Severity, "Hint", StringComparison.OrdinalIgnoreCase));
        var reportable = allFindings
            .Where(f => !string.Equals(f.Severity, "Hint", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var errorCount = reportable.Count(f => string.Equals(f.Severity, "Error", StringComparison.OrdinalIgnoreCase));
        var warningCount = reportable.Count(f => string.Equals(f.Severity, "Warning", StringComparison.OrdinalIgnoreCase));
        // "proven" is V-04's verdict, never "no errors": a write analysis could not verify is unknown.
        var verification = workspaceState.LatestAnalysis?.GetVerification(workspaceState.Domain, scopePath);
        var unverified = verification?.UnverifiedWrites.Count ?? 0;
        var verdict = !selectMatched
            ? "unknown"
            : !string.IsNullOrWhiteSpace(args)
                ? "unknown"
                : errorCount > 0 ? "violated"
                : verification?.Verdict == CheckVerdict.Proven ? "proven" : "unknown";

        var summary = verdict switch {
            "violated" => $"Violated: {errorCount} error(s), {warningCount} warning(s), {hintCount} hint(s).",
            "unknown" when !selectMatched => $"Unknown: no finding matches '{select}'.",
            "unknown" when !string.IsNullOrWhiteSpace(args) => "Unknown: argument-specific proofs are not implemented yet; use poly_simulate to execute the action.",
            "unknown" => $"Unknown: {unverified} finding(s) could not be verified (writes analysis could not prove inside their constraints, or a default() beside a range or length), {warningCount} warning(s), {hintCount} hint(s). Bound the source, widen the target or guard a non-parameter write; poly_simulate executes the action.",
            _ => $"Proven: no errors, every write verified, {warningCount} warning(s), {hintCount} hint(s).",
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
                unverifiedFindings = unverified,
                findingsTotal = reportable.Count,
                truncated = reportable.Count > FindingCap,
                witness,
            },
            Next: verdict == "violated"
                ? Next(workspace, "poly_guide", "poly_define")
                : Next(workspace, "poly_export", "poly_simulate"),
            State: StateOf(workspace));
    }

    // ── 5. inspect ──────────────────────────────────────────────

    [McpServerTool(Name = "poly_inspect", Title = "Inspect the model", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PolyOutputSchema)), Description("Reads one part of the model: omit select for an overview, or pass one element path — an entity, enum, value type or function by name, \"Entity.Member\" for a property, navigation, stage, action or policy, or \"Entity.Stage.Action\" for a stage action. A path that is unknown, ambiguous or not addressable returns ELEMENT_NOT_FOUND with the reason and candidates. Brief by default returns the one-line summary; detail=\"full\" returns the full element. Read-only.")]
    public PolyResponse Inspect(
        [Description("Workspace handle from poly_workspace.")] string workspace,
        [Description("Optional element path: \"Entity\", \"Entity.Member\", \"Entity.Stage.Action\", or an enum, value type or function name. Case-sensitive; \".\" and \"/\" both separate segments.")] string? select = null,
        [Description("brief (default) or full.")] string? detail = null) {
        if (!TryWorkspace(workspace, out var state))
            return WorkspaceRequired();

        var outcome = InspectService.Inspect(workspace, state, select, _query);
        var brief = !string.Equals(detail, "full", StringComparison.OrdinalIgnoreCase);
        var data = !string.IsNullOrWhiteSpace(select) && brief && outcome.ErrorCode is null
            ? new { description = outcome.Summary }
            : outcome.Data;
        return new PolyResponse(
            Ok: outcome.Ok,
            Summary: outcome.Summary,
            Findings: [],
            Data: data,
            Next: outcome.Ok ? Next(workspace, "poly_inspect", "poly_check") : Next(workspace, "poly_check"),
            State: StateOf(workspace),
            ErrorCode: outcome.ErrorCode);
    }

    // ── 6. export ───────────────────────────────────────────────

    [McpServerTool(Name = "poly_export", Title = "Export an artifact", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PolyOutputSchema)), Description("Produces the artifact the task needs from the current model: dsl, json-schema, csharp, markdown, html, json-ld, or shacl. Small artifacts are returned inline; anything over 4 KB is written to a temp file named by its sha256 and returned as {path, bytes, sha256}, so identical content reuses one file and poly_workspace op=close removes the files the workspace wrote. Read-only; the model is unchanged.")]
    public PolyResponse Export(
        [Description("Workspace handle from poly_workspace.")] string workspace,
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
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            var name = $"poly-{Sanitize(workspace)}-{normalized}-{hash[..16]}.{extension}";
            var path = Path.Combine(Path.GetTempPath(), name);
            if (!File.Exists(path))
                File.WriteAllText(path, content);
            _exportedFiles.GetOrAdd(workspace, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal))
                .TryAdd(path, 0);
            data = new { format = normalized, path, bytes, sha256 = hash };
        }
        else {
            data = new { format = normalized, content, bytes };
        }

        return new PolyResponse(
            Ok: true,
            Summary: normalized == "csharp"
                ? $"Exported csharp ({bytes} bytes); compile it against {CSharpHostDependencies.Requirement(ExportFileNames(response))}."
                : $"Exported {normalized} ({bytes} bytes).",
            Findings: [],
            Data: data,
            Next: Next(workspace, "poly_inspect", "poly_check"),
            State: StateOf(workspace));
    }

    // ── 7. simulate ─────────────────────────────────────────────

    /// <summary>
    /// The keys each step op reads, in the order the tool description lists them. A step object's
    /// keys outside this set are a caller error: dropping them silently turned a misspelled key
    /// into a different, successful scenario (#536).
    /// </summary>
    private static readonly Dictionary<string, string[]> StepKeys = new(StringComparer.Ordinal) {
        ["create"] = ["op", "entity", "properties", "as"],
        ["link"] = ["op", "sourceInstanceId", "relationship", "targetInstanceId"],
        ["unlink"] = ["op", "sourceInstanceId", "relationship", "targetInstanceId"],
        ["invoke"] = ["op", "instanceId", "action", "args"],
        ["evaluate"] = ["op", "entity", "policy", "instanceId"],
        ["get"] = ["op", "instanceId"],
        ["list"] = ["op", "entity"],
    };

    [McpServerTool(Name = "poly_simulate", Title = "Run a scenario", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PolyOutputSchema)), Description("Runs an ordered scenario against the model and returns one trace entry per step. steps is a JSON array of objects; every object carries op plus that op's own keys, and any other key is a failed step that names the valid ones: " +
        "create {entity, properties?, as?}; link {sourceInstanceId, relationship, targetInstanceId}; unlink (the same keys as link); " +
        "invoke {instanceId, action, args?}; evaluate {entity, policy, instanceId}; get {instanceId}; list {entity?}. " +
        "properties and args are JSON objects of property and parameter values. An instanceId takes an instance id or a create's as alias; a singular navigation in properties takes one too. " +
        "A guard-blocked step is a failed step, not a failed call; earlier steps stay committed. reset=true clears the instances first; a poly_define clears them too.")]
    public PolyResponse Simulate(
        [Description("Workspace handle from poly_workspace.")] string workspace,
        [Description("JSON array of steps, e.g. [{\"op\":\"create\",\"entity\":\"Product\",\"properties\":{\"Total\":42},\"as\":\"p\"}]. " +
            "A create's properties may name a singular navigation whose value is an instance id or an earlier step's alias, " +
            "e.g. {\"Name\":\"Grace\",\"account\":\"acme\"}; a required navigation must be given at create. " +
            "An invoke's entity-typed args value takes an instance id or an earlier step's alias the same way.")] string steps,
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
                if (StepKeys.TryGetValue(op, out var validKeys)) {
                    var unknownKeys = step.EnumerateObject()
                        .Select(property => property.Name)
                        .Where(name => !validKeys.Contains(name, StringComparer.Ordinal))
                        .ToList();
                    if (unknownKeys.Count > 0)
                        throw new InvalidOperationException(
                            $"Unknown key(s) {string.Join(", ", unknownKeys.Select(name => $"'{name}'"))} for op '{op}'. "
                            + $"Valid keys: {string.Join(", ", validKeys)}.");
                }
                response = op switch {
                    "create" => _runtime.CreateInstance(
                        workspace,
                        Required(step, "entity"),
                        OptionalJson(step, "properties"),
                        aliases),
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
                        OptionalJson(step, "args"),
                        aliases),
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
            Summary: "A workspace handle is required. Call poly_workspace first, then pass the returned handle as workspace.",
            Findings: [],
            Data: null,
            Next: ["poly_workspace"],
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
                .Select(f => new PolyFinding(f.Severity, f.Code, f.ElementPath, f.Message, Line: f.Line, Column: f.Column))
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
        || path.Contains("/" + normalized + "/", StringComparison.Ordinal)
        || StageActionPathMatches(path, normalized);

    /// <summary>
    /// #481: "Entity.Action" selects that action wherever it runs, so it also matches the action's
    /// own findings inside a stage ("Domain/Entity/Stage/Action"). Findings carry the stage segment;
    /// the unqualified select does not.
    /// </summary>
    private static bool StageActionPathMatches(string path, string normalized) {
        var selectParts = normalized.Split('/');
        var pathParts = path.Split('/');
        return selectParts.Length == 2
            && pathParts.Length == 4
            && string.Equals(pathParts[1], selectParts[0], StringComparison.Ordinal)
            && string.Equals(pathParts[3], selectParts[1], StringComparison.Ordinal);
    }

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

    /// <summary>
    /// The file names a producing tool reported in <c>Data.files</c>, so a success summary can
    /// name what the artifact needs to compile.
    /// </summary>
    private static IReadOnlyList<string> ExportFileNames(DomainToolResponse response) {
        var files = JsonSerializer.SerializeToNode(response.Data)?["files"] as JsonArray;
        return files?.Select(file => file?.GetValue<string>()).OfType<string>().ToList() ?? [];
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

    /// <summary>Deletes the artifact files this workspace wrote; a failed delete never fails the close.</summary>
    private void RemoveExportedFiles(string workspace) {
        if (!_exportedFiles.TryRemove(workspace, out var files))
            return;
        foreach (var path in files.Keys) {
            try {
                File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private const string Quickstart = """
        Poly models a domain as one executable .poly document: entities, typed properties
        with constraints, stages, actions, and policies. Write the domain down, let the
        analyzer prove it, then export or simulate it.

        Workflow: poly_guide -> poly_workspace -> poly_define(poly) -> poly_check -> poly_export or poly_simulate.

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