using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol.Server;

using Poly.Mcp.Surface;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Mcp;

/// <summary>
/// <see cref="PolySurfaceHarness"/>: the v2 MCP surface in process with typed results. The
/// stdio wire contract is <c>McpProtocolTests</c>' job; the one wire test here pins that the harness
/// decodes the wire envelope into a typed <c>Code</c>.
/// </summary>
public class PolySurfaceHarnessTests {
    private const string SellableShape = """
        domain Catalog
        uses temporal
        uses storage
        uses sqlite
        uses http

        Item: entity {
          Name: Text
          Qty: Number
          Bump: action { assign Qty to Qty + 1 }
        }
        """;

    private const string Valid = """
        domain V2
        Product: entity {
          Total: Number range(0, 100)
          Note: Text optional
          Draft: stage { Submit: action { transition to Active } }
          Active: stage {}
        }
        """;

    private const string Invalid = """
        domain V2Bad
        Product: entity {
          Total: Number range(0, 100)
          Raise: action { assign Total to Total + 250 }
        }
        """;

    private const string SellableSteps = """
        [
          {"op":"create","entity":"Item","properties":{"Name":"Widget","Qty":1},"as":"w"},
          {"op":"invoke","instanceId":"w","action":"Bump"},
          {"op":"get","instanceId":"w"}
        ]
        """;

    [Test]
    public async Task Define_Check_Simulate_Export_RoundTrip_OnTheSellableShape() {
        var h = PolySurfaceHarness.New();
        var ws = h.Open("Catalog");

        var define = h.Define(ws, SellableShape);
        await Assert.That(define.Success).IsTrue().Because(define.Summary);
        await Assert.That(define.Code).IsNull();
        await Assert.That(define.Revision).IsEqualTo(1L);

        var check = h.Check(ws);
        await Assert.That(check.Success).IsTrue().Because(check.Summary);
        await Assert.That(check.Data!["errorCount"]!.GetValue<int>()).IsEqualTo(0);
        await Assert.That(check.Data["verdict"]!.GetValue<string>()).IsEqualTo("proven");

        var simulate = h.Simulate(ws, SellableSteps);
        await Assert.That(simulate.Success).IsTrue().Because(simulate.Summary);
        var trace = simulate.Trace!;
        await Assert.That(trace.Count).IsEqualTo(3);
        var failed = trace.Where(t => t!["ok"]!.GetValue<bool>() is false).Select(t => t!["summary"]!.GetValue<string>()).ToList();
        await Assert.That(failed).IsEmpty().Because(string.Join("; ", failed));
        var qty = trace[2]!["data"]!["instance"]!["properties"]!.AsArray()
            .Single(p => p!["name"]!.GetValue<string>() == "Qty")!["value"]!.GetValue<string>();
        await Assert.That(qty).IsEqualTo("2");

        var dsl = h.Export(ws, "dsl");
        await Assert.That(dsl.Success).IsTrue().Because(dsl.Summary);
        await Assert.That(dsl.Content).Contains("uses storage");
        await Assert.That(dsl.Content).Contains("uses sqlite");
        await Assert.That(dsl.Content).Contains("uses http");

        var csharp = h.Export(ws, "csharp");
        await Assert.That(csharp.Success).IsTrue().Because(csharp.Summary);
        await Assert.That(csharp.Content).IsNotNull();
        await Assert.That(csharp.Content!).Contains("class Item");
    }

    /// <summary>
    /// The only wire test here. Rows overlap <c>McpProtocolTests.CallerErrors_ReturnTypedErrors_AtTheWire</c>
    /// on purpose: that test pins the wire, this one pins the harness decode. Envelope fields only,
    /// never Data: the wire casing differs from the in-process one.
    /// </summary>
    [Test]
    public async Task Harness_ReturnsTypedErrorCodes_ForWrongTypeAndMissingArgs() {
        using var host = await PolycProcess.McpAsync(TimeSpan.FromSeconds(20));
        await host.InitializeAsync();
        var opened = HarnessResult.FromEnvelope(await host.ToolAsync("poly_workspace", new JsonObject { ["name"] = "Wire" }));
        var workspace = opened.Workspace!;

        var rows = new (string Row, string Tool, JsonObject Arguments, string Code, string[] Fragments)[] {
            ("reset:\"yes\" names reset/boolean", "poly_simulate",
                new JsonObject { ["workspace"] = workspace, ["steps"] = "[]", ["reset"] = "yes" },
                "INVALID_ARGUMENT", ["reset", "boolean"]),
            ("a missing required steps", "poly_simulate",
                new JsonObject { ["workspace"] = workspace },
                "INVALID_ARGUMENT", ["steps"]),
            ("a missing required workspace", "poly_define",
                new JsonObject { ["poly"] = Valid },
                "WORKSPACE_REQUIRED", ["poly_workspace"]),
            ("an unknown argument names the valid ones", "poly_guide",
                new JsonObject { ["bogus"] = 1 },
                "UNKNOWN_ARGUMENT", ["Valid: topic"]),
        };

        var observed = new List<HarnessResult>();
        foreach (var row in rows)
            observed.Add(HarnessResult.FromEnvelope(await host.ToolAsync(row.Tool, row.Arguments)));

        var table = string.Join("\n", rows.Select((row, i) => $"  {row.Row} => {observed[i].Code}: {observed[i].Summary}"));
        await Assert.That(opened.Success).IsTrue().Because(opened.Summary);
        for (var i = 0; i < rows.Length; i++) {
            await Assert.That(observed[i].Success).IsFalse().Because($"{rows[i].Row}\nobserved:\n{table}");
            await Assert.That(observed[i].Code).IsEqualTo(rows[i].Code).Because($"{rows[i].Row}\nobserved:\n{table}");
            foreach (var fragment in rows[i].Fragments)
                await Assert.That(observed[i].Summary).Contains(fragment).Because($"{rows[i].Row}\nobserved:\n{table}");
        }

        await Assert.That(observed[0].Workspace).IsEqualTo(workspace).Because("a validator error still carries the state envelope");
    }

    [Test]
    public async Task Harness_TypedMethods_ReturnTheTypedCodes() {
        var h = PolySurfaceHarness.New();
        var closed = h.Open("Closed");
        await Assert.That(h.Workspace(op: "close", workspace: closed).Success).IsTrue();
        var fresh = h.Open("Fresh");

        var rows = new (string Row, HarnessResult Result, string Code)[] {
            ("an empty handle", h.Inspect(""), "WORKSPACE_REQUIRED"),
            ("a closed handle", h.Define(closed, Valid), "WORKSPACE_REQUIRED"),
            ("closing an unknown handle", h.Workspace(op: "close", workspace: "nope"), "WORKSPACE_NOT_FOUND"),
            ("an unknown op", h.Workspace(op: "x"), "UNKNOWN_OP"),
            ("poly and edit together", h.Define(fresh, poly: Valid, edit: "[]"), "EDIT_ARGUMENT_CONFLICT"),
            ("an edit before any apply", h.Define(fresh, edit: """[{"op":"add","parent":"","dsl":"uses storage"}]"""), "EDIT_NO_SOURCE"),
        };

        var table = string.Join("\n", rows.Select(r => $"  {r.Row} => {r.Result.Code}: {r.Result.Summary}"));
        foreach (var row in rows) {
            await Assert.That(row.Result.Success).IsFalse().Because($"{row.Row}\nobserved:\n{table}");
            await Assert.That(row.Result.Code).IsEqualTo(row.Code).Because($"{row.Row}\nobserved:\n{table}");
        }
    }

    /// <summary>
    /// Derived, not listed: every published tool method needs a same-named harness method taking
    /// every one of its parameters, so a new tool or a renamed argument fails here.
    /// </summary>
    [Test]
    public async Task Harness_CoversEveryPublishedTool() {
        var tools = typeof(PolySurfaceTools).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToList();
        await Assert.That(tools).IsNotEmpty();

        var harnessMethods = typeof(PolySurfaceHarness).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var gaps = new List<string>();
        foreach (var tool in tools) {
            var counterpart = harnessMethods.Where(m => m.Name == tool.Name).ToList();
            if (counterpart.Count == 0) {
                gaps.Add($"{tool.Name}: no harness method");
                continue;
            }

            var harnessParameters = counterpart.SelectMany(m => m.GetParameters()).Select(p => p.Name!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in tool.GetParameters())
                if (!harnessParameters.Contains(parameter.Name!))
                    gaps.Add($"{tool.Name}: harness has no '{parameter.Name}' parameter");
        }

        await Assert.That(gaps).IsEmpty().Because(string.Join("; ", gaps));
    }

    [Test]
    public async Task Harness_Edit_RoundTrips() {
        var h = PolySurfaceHarness.New();
        var ws = h.Open("Catalog");
        await Assert.That(h.Define(ws, SellableShape).Success).IsTrue();

        var edited = h.Edit(ws, """[{"op":"add","parent":"Item","dsl":"Amount: Number"}]""");

        await Assert.That(edited.Success).IsTrue().Because(edited.Summary);
        await Assert.That(edited.Revision).IsEqualTo(2L);
        var dsl = h.Export(ws, "dsl");
        await Assert.That(dsl.Content).Contains("Amount");
    }

    [Test]
    public async Task Harness_Inspect_OverviewAndEntity() {
        var h = PolySurfaceHarness.New();
        var ws = h.Open("Catalog");
        await Assert.That(h.Define(ws, SellableShape).Success).IsTrue();

        var overview = h.Inspect(ws);
        var entity = h.Inspect(ws, "Item");

        await Assert.That(overview.Success).IsTrue().Because(overview.Summary);
        await Assert.That(overview.Data!["entityNames"]!.AsArray().Select(n => n!.GetValue<string>())).Contains("Item");
        await Assert.That(entity.Success).IsTrue().Because(entity.Summary);
        await Assert.That(entity.Summary).Contains("Item");
    }

    [Test]
    public async Task Harness_RejectedDefine_ReportsFindings_NotAnException() {
        var h = PolySurfaceHarness.New();
        var ws = h.Open("V2");
        var applied = h.Define(ws, Valid);
        await Assert.That(applied.Success).IsTrue().Because(applied.Summary);

        var rejected = h.Define(ws, Invalid);

        await Assert.That(rejected.Success).IsFalse();
        await Assert.That(rejected.Findings.Any(f => f.Code == "DMEFF008")).IsTrue()
            .Because(string.Join("; ", rejected.Findings.Select(f => f.Code)));
        await Assert.That(rejected.Revision).IsEqualTo(applied.Revision);
    }

    /// <summary>Green at birth: the harness adds no behaviour over the typed surface (one accepted, one rejected call).</summary>
    [Test]
    public async Task Harness_Parity_WithTheTypedSurface() {
        var h = PolySurfaceHarness.New();
        var hws = h.Open("Parity");
        var direct = new PolySurfaceTools(
            new Poly.Mcp.Sessions.McpSessionStore(
                TimeProvider.System,
                Microsoft.Extensions.Options.Options.Create(new Poly.Mcp.Sessions.McpSessionOptions()),
                NullLogger<Poly.Mcp.Sessions.McpSessionStore>.Instance),
            NullLogger<PolySurfaceTools>.Instance);
        var dws = direct.Workspace(name: "Parity").State!.Workspace;

        foreach (var poly in new[] { Valid, Invalid }) {
            var viaHarness = h.Define(hws, poly);
            var viaSurface = direct.Define(dws, poly);

            await Assert.That(viaHarness.Success).IsEqualTo(viaSurface.Ok);
            await Assert.That(viaHarness.Code).IsEqualTo(viaSurface.ErrorCode);
            await Assert.That(viaHarness.Summary).IsEqualTo(viaSurface.Summary);
            await Assert.That(viaHarness.Findings.Select(f => (f.Severity, f.Code, f.Path, f.Message, f.Line, f.Column)))
                .IsEquivalentTo(viaSurface.Findings.Select(f => (f.Severity, f.Code, f.Path, f.Message, f.Line, f.Column)));
            await Assert.That(viaHarness.Revision).IsEqualTo(viaSurface.State?.Revision);
            await Assert.That(viaHarness.Next.Select(n => n.Replace(hws, "<ws>")))
                .IsEquivalentTo(viaSurface.Next.Select(n => n.Replace(dws, "<ws>")));
            await Assert.That(viaHarness.Data?.ToJsonString()).IsEqualTo(JsonSerializer.SerializeToNode(viaSurface.Data)?.ToJsonString());
        }
    }
}