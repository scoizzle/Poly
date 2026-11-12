using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Poly.Mcp.Sessions;
using Poly.Mcp.Surface;

namespace Poly.Tests.TestHelpers;

/// <summary>One diagnostic from the v2 envelope's <c>findings</c>.</summary>
internal sealed record HarnessFinding(string Severity, string? Code, string Path, string Message, int? Line, int? Column, string? Hint, string? Fix);

/// <summary>
/// The v2 envelope as a typed value. <see cref="Code"/> is the envelope <c>errorCode</c> verbatim
/// (no synthesis); the per-diagnostic codes are in <see cref="Findings"/>. <see cref="Data"/> keeps
/// the transport's own casing, so a test never reads it across transports.
/// </summary>
internal sealed record HarnessResult(
    bool Success,
    string? Code,
    string Summary,
    IReadOnlyList<HarnessFinding> Findings,
    JsonNode? Data,
    string? Workspace,
    long? Revision,
    IReadOnlyList<string> Next,
    string? Model,
    int? Entities,
    int? Instances) {
    /// <summary>The one decoder: an absent member and an explicit null are the same thing.</summary>
    public static HarnessResult FromEnvelope(JsonNode envelope) {
        var root = envelope.AsObject();
        var state = root["state"] as JsonObject;
        var findings = (root["findings"] as JsonArray ?? [])
            .Select(f => f!.AsObject())
            .Select(f => new HarnessFinding(
                f["severity"]?.GetValue<string>() ?? "",
                f["code"]?.GetValue<string>(),
                f["path"]?.GetValue<string>() ?? "",
                f["message"]?.GetValue<string>() ?? "",
                f["line"]?.GetValue<int>(),
                f["column"]?.GetValue<int>(),
                f["hint"]?.GetValue<string>(),
                f["fix"]?.GetValue<string>()))
            .ToList();
        var next = (root["next"] as JsonArray ?? [])
            .Select(n => n!.GetValue<string>())
            .ToList();
        return new HarnessResult(
            Success: root["ok"]?.GetValue<bool>() ?? false,
            Code: root["errorCode"]?.GetValue<string>(),
            Summary: root["summary"]?.GetValue<string>() ?? "",
            Findings: findings,
            Data: root["data"],
            Workspace: state?["workspace"]?.GetValue<string>(),
            Revision: state?["revision"]?.GetValue<long>(),
            Next: next,
            Model: state?["model"]?.GetValue<string>(),
            Entities: state?["entities"]?.GetValue<int>(),
            Instances: state?["instances"]?.GetValue<int>());
    }

    /// <summary>An export's inline <c>content</c>.</summary>
    public string? Content => Data?["content"]?.GetValue<string>();

    /// <summary>A simulation's per-step <c>trace</c>.</summary>
    public JsonArray? Trace => Data?["trace"] as JsonArray;
}

/// <summary>
/// The v2 MCP surface, in process, with typed results. It wraps the public tool methods of
/// <see cref="PolySurfaceTools"/> over its own session store and adds no behaviour: each method
/// serialises the tool's <see cref="PolyResponse"/> and decodes it through
/// <see cref="HarnessResult.FromEnvelope"/>. The wire contract is <c>PolycProcess.McpAsync</c>'s job.
/// </summary>
internal sealed class PolySurfaceHarness {
    private readonly PolySurfaceTools _surface;

    private PolySurfaceHarness(McpSessionStore store) {
        Store = store;
        _surface = new PolySurfaceTools(store, NullLogger<PolySurfaceTools>.Instance);
    }

    /// <summary>The session store this harness owns, for tests that need to look underneath.</summary>
    public McpSessionStore Store { get; }

    public static PolySurfaceHarness New() =>
        new(new McpSessionStore(
            TimeProvider.System,
            Options.Create(new McpSessionOptions()),
            NullLogger<McpSessionStore>.Instance));

    /// <summary>Creates a workspace and returns its handle.</summary>
    public string Open(string? name = null) =>
        Workspace(name: name).Workspace
            ?? throw new InvalidOperationException("poly_workspace returned no handle.");

    public HarnessResult Guide(string? topic = null) => Decode(_surface.Guide(topic));

    public HarnessResult Workspace(string? op = null, string? name = null, string? workspace = null) =>
        Decode(_surface.Workspace(op, name, workspace));

    public HarnessResult Define(string workspace, string? poly = null, bool apply = true, string? edit = null) =>
        Decode(_surface.Define(workspace, poly, apply, edit));

    /// <summary>Sugar over <see cref="Define"/> with <c>edit</c>.</summary>
    public HarnessResult Edit(string workspace, string ops, bool apply = true) =>
        Define(workspace, edit: ops, apply: apply);

    public HarnessResult Check(
        string workspace, string? select = null, string? args = null, string? expression = null, string? bindings = null) =>
        Decode(_surface.Check(workspace, select, args, expression, bindings));

    public HarnessResult Inspect(string workspace, string? select = null, string? detail = null) =>
        Decode(_surface.Inspect(workspace, select, detail));

    /// <summary>Inline by default: the harness never reads a temp-file <c>path</c>.</summary>
    public HarnessResult Export(string workspace, string format, bool inline = true) =>
        Decode(_surface.Export(workspace, format, inline));

    public HarnessResult Simulate(string workspace, string steps, bool reset = false, bool stopOnError = true) =>
        Decode(_surface.Simulate(workspace, steps, reset, stopOnError));

    private static HarnessResult Decode(PolyResponse response) =>
        HarnessResult.FromEnvelope(JsonSerializer.SerializeToNode(response)!);
}