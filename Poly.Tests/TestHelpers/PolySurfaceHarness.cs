using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Poly.Mcp.Sessions;
using Poly.Mcp.Surface;

namespace Poly.Tests.TestHelpers;

/// <summary>One diagnostic from the v2 envelope's <c>findings</c>.</summary>
internal sealed record HarnessFinding(string Severity, string? Code, string Path, string Message, int? Line, int? Column);

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
    IReadOnlyList<string> Next) {
    /// <summary>The one decoder: an absent member and an explicit null are the same thing.</summary>
    public static HarnessResult FromEnvelope(JsonNode envelope) {
        throw new NotImplementedException();
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
    public string Open(string? name = null) => throw new NotImplementedException();

    public HarnessResult Guide(string? topic = null) => throw new NotImplementedException();

    public HarnessResult Workspace(string? op = null, string? name = null, string? workspace = null) => throw new NotImplementedException();

    public HarnessResult Define(string workspace, string? poly = null, bool apply = true, string? edit = null) => throw new NotImplementedException();

    /// <summary>Sugar over <see cref="Define"/> with <c>edit</c>.</summary>
    public HarnessResult Edit(string workspace, string ops, bool apply = true) => throw new NotImplementedException();

    public HarnessResult Check(
        string workspace, string? select = null, string? args = null, string? expression = null, string? bindings = null) => throw new NotImplementedException();

    public HarnessResult Inspect(string workspace, string? select = null, string? detail = null) => throw new NotImplementedException();

    /// <summary>Inline by default: the harness never reads a temp-file <c>path</c>.</summary>
    public HarnessResult Export(string workspace, string format, bool inline = true) => throw new NotImplementedException();

    public HarnessResult Simulate(string workspace, string steps, bool reset = false, bool stopOnError = true) => throw new NotImplementedException();

    private static HarnessResult Decode(PolyResponse response) => throw new NotImplementedException();
}