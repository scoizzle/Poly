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
/// <param name="Success">The envelope's <c>ok</c>. For a simulation it only means the scenario ran: assert a step through <c>Step(i).Ok</c>.</param>
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

    /// <summary>
    /// A simulation's trace as typed steps. Throws when the response carried no trace, or a trace entry
    /// lacks <c>index</c>, <c>op</c>, <c>ok</c> or <c>summary</c>; it never returns an empty list for a missing trace.
    /// </summary>
    public IReadOnlyList<HarnessStep> Steps {
        get {
            if (Trace is null)
                throw new InvalidOperationException(
                    $"The response has no trace (ok={Success}, code={Code ?? "none"}): {Summary}");
            return Trace
                .Select(t => t!.AsObject())
                .Select(t => new HarnessStep(
                    Required<int>(t, "index"), Required<string>(t, "op"), Required<bool>(t, "ok"), Required<string>(t, "summary"), t["data"]))
                .ToList();
        }
    }

    private static T Required<T>(JsonObject entry, string name) =>
        entry[name] is { } node
            ? node.GetValue<T>()
            : throw new InvalidOperationException($"A trace entry has no '{name}': {entry.ToJsonString()}");

    /// <summary>
    /// One trace entry. A response with no trace (WORKSPACE_REQUIRED, bad steps JSON) throws a
    /// descriptive error carrying the envelope's own code and summary.
    /// </summary>
    public HarnessStep Step(int index) {
        if (Trace is null)
            throw new InvalidOperationException(
                $"Step({index}): the response has no trace (ok={Success}, code={Code ?? "none"}): {Summary}");
        var steps = Steps;
        if (index < 0 || index >= steps.Count)
            throw new InvalidOperationException($"Step({index}): the trace has {steps.Count} step(s).");
        return steps[index];
    }
}

/// <summary>The outcome of an <c>invoke</c> step, from <c>invokeActionResult</c> or the thrown-action failure shape.</summary>
internal sealed record InvokeOutcome(
    bool Succeeded, string? ErrorMessage, string? NewStage, IReadOnlyList<string> FailedGuards,
    string? ReturnTypeName = null, string? ReturnInstanceId = null);

/// <summary>One entry of a simulation's <c>trace</c>: a step's <c>ok</c>, <c>summary</c> and <c>data</c>.</summary>
internal sealed record HarnessStep(int Index, string Op, bool Ok, string Summary, JsonNode? Data) {
    /// <summary>The instance a create or get step returned: <c>Data.instance.instanceId</c>.</summary>
    public string InstanceId =>
        Data?["instance"]?["instanceId"]?.GetValue<string>()
            ?? throw new InvalidOperationException($"Step {Index} ({Op}) has no instance id (ok={Ok}): {Summary}");

    /// <summary>
    /// A rendered property value from <c>Data.instance.properties[]</c>. Throws, naming the property and the
    /// step, when the step has no instance snapshot or the name is absent. Use <see cref="TryProperty"/> to ask.
    /// </summary>
    public string Property(string name) =>
        TryProperty(name, out var value)
            ? value!
            : throw new InvalidOperationException(
                $"Step {Index} ({Op}) has no property '{name}' (ok={Ok}): {Summary}");

    /// <summary>The deliberate absence check: false when the step has no snapshot or no such property.</summary>
    public bool TryProperty(string name, out string? value) {
        value = (Data?["instance"]?["properties"] as JsonArray)?
            .FirstOrDefault(p => p?["name"]?.GetValue<string>() == name)?["value"]?.GetValue<string>();
        return value is not null;
    }

    /// <summary>
    /// An invoke step's outcome: <c>Data.invokeActionResult</c>, or the <c>{actionName, error}</c> shape of an
    /// action that threw. Throws (step index, op, summary) for any other step, including an invoke that never
    /// reached the module: assert those through <see cref="Ok"/> and <see cref="Summary"/>.
    /// </summary>
    public InvokeOutcome Result {
        get {
            if (Op == "invoke" && Data?["invokeActionResult"] is JsonObject result)
                return new InvokeOutcome(
                    result["succeeded"]?.GetValue<bool>()
                        ?? throw new InvalidOperationException($"Step {Index} (invoke): invokeActionResult has no 'succeeded'."),
                    result["errorMessage"]?.GetValue<string>(),
                    result["newStage"]?.GetValue<string>(),
                    (result["failedGuards"] as JsonArray)?.Select(g => g!.GetValue<string>()).ToList() ?? [],
                    result["returnTypeName"]?.GetValue<string>(),
                    result["returnInstanceId"]?.GetValue<string>());
            if (Op == "invoke" && Data?["error"] is { } error && Data["actionName"] is not null)
                return new InvokeOutcome(false, error.GetValue<string>(), null, []);
            throw new InvalidOperationException(
                $"Step {Index} ({Op}) has no invoke outcome (ok={Ok}): {Summary}");
        }
    }
}

/// <summary>A simulate step the harness serialises, so no test hand-writes step JSON.</summary>
internal sealed record SimStep(JsonObject Json) {
    public static SimStep Create(string entity, string? propertiesJson = null, string? alias = null) =>
        Of("create", ("entity", entity), ("properties", Parse(propertiesJson)), ("as", alias));

    public static SimStep Invoke(string instanceId, string action, string? argsJson = null) =>
        Of("invoke", ("instanceId", instanceId), ("action", action), ("args", Parse(argsJson)));

    public static SimStep Link(string source, string relationship, string target) =>
        Of("link", ("sourceInstanceId", source), ("relationship", relationship), ("targetInstanceId", target));

    public static SimStep Unlink(string source, string relationship, string target) =>
        Of("unlink", ("sourceInstanceId", source), ("relationship", relationship), ("targetInstanceId", target));

    public static SimStep Evaluate(string entity, string policy, string instanceId) =>
        Of("evaluate", ("entity", entity), ("policy", policy), ("instanceId", instanceId));

    public static SimStep Get(string instanceId) => Of("get", ("instanceId", instanceId));

    public static SimStep List(string? entity = null) => Of("list", ("entity", entity));

    private static JsonNode? Parse(string? json) => json is null ? null : JsonNode.Parse(json);

    private static SimStep Of(string op, params (string Name, object? Value)[] members) {
        var json = new JsonObject { ["op"] = op };
        foreach (var (name, value) in members) {
            if (value is null)
                continue;
            json[name] = value is JsonNode node ? node : JsonValue.Create((string)value);
        }
        return new SimStep(json);
    }
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

    /// <summary>A harness over an existing store, so a test that makes its workspace there sees the same state.</summary>
    public static PolySurfaceHarness Over(McpSessionStore store) => new(store);

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

    /// <remarks>Assert a step through <c>Step(i).Ok</c>; <c>Success</c> only means the scenario ran, not that every step did.</remarks>
    public HarnessResult Simulate(string workspace, string steps, bool reset = false, bool stopOnError = true) =>
        Decode(_surface.Simulate(workspace, steps, reset, stopOnError));

    /// <remarks>Assert a step through <c>Step(i).Ok</c>; <c>Success</c> only means the scenario ran, not that every step did.</remarks>
    public HarnessResult Simulate(string workspace, params SimStep[] steps) =>
        Simulate(workspace, steps, reset: false, stopOnError: true);

    /// <remarks>Assert a step through <c>Step(i).Ok</c>; <c>Success</c> only means the scenario ran, not that every step did.</remarks>
    public HarnessResult Simulate(string workspace, SimStep[] steps, bool reset = false, bool stopOnError = true) =>
        Simulate(workspace, new JsonArray(steps.Select(step => (JsonNode?)step.Json.DeepClone()).ToArray()).ToJsonString(), reset, stopOnError);

    public HarnessStep Create(string workspace, string entity, string? propertiesJson = null) =>
        One(workspace, SimStep.Create(entity, propertiesJson));

    public HarnessStep Invoke(string workspace, string instanceId, string action, string? argsJson = null) =>
        One(workspace, SimStep.Invoke(instanceId, action, argsJson));

    public HarnessStep Link(string workspace, string source, string relationship, string target) =>
        One(workspace, SimStep.Link(source, relationship, target));

    public HarnessStep Unlink(string workspace, string source, string relationship, string target) =>
        One(workspace, SimStep.Unlink(source, relationship, target));

    public HarnessStep Evaluate(string workspace, string entity, string policy, string instanceId) =>
        One(workspace, SimStep.Evaluate(entity, policy, instanceId));

    public HarnessStep Get(string workspace, string instanceId) =>
        One(workspace, SimStep.Get(instanceId));

    public HarnessStep List(string workspace, string? entity = null) =>
        One(workspace, SimStep.List(entity));

    private HarnessStep One(string workspace, SimStep step) => Simulate(workspace, step).Step(0);

    private static HarnessResult Decode(PolyResponse response) =>
        HarnessResult.FromEnvelope(JsonSerializer.SerializeToNode(response)!);
}