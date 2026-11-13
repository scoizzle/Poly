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

        await Assert.That(h.Store.TryGet(ws, out var state)).IsTrue();
        await Assert.That(state.Domain.Extensions).Contains("sqlite");
        await Assert.That(state.Domain.Extensions).Contains("http");
        await Assert.That(state.Modeling.Extensions).Contains("sqlite");
        await Assert.That(state.Modeling.Extensions).Contains("http");

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

        await Assert.That(observed[2].Next).Contains("poly_workspace");
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
        await Assert.That(tools.Count).IsEqualTo(7);

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
        await Assert.That(dsl.Content).Contains("Amount: Number");
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
        await Assert.That(entity.Summary).Contains("Entity 'Item'");
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
            await Assert.That(viaHarness.Findings.Select(f => (f.Hint, f.Fix)))
                .IsEquivalentTo(viaSurface.Findings.Select(f => (f.Hint, f.Fix)));
            await Assert.That(viaHarness.Revision).IsEqualTo(viaSurface.State?.Revision);
            await Assert.That(viaHarness.Workspace).IsEqualTo(hws);
            await Assert.That(viaHarness.Model).IsEqualTo(viaSurface.State?.Model);
            await Assert.That(viaHarness.Entities).IsEqualTo(viaSurface.State?.Entities);
            await Assert.That(viaHarness.Instances).IsEqualTo(viaSurface.State?.Instances);
            await Assert.That(viaHarness.Next.Select(n => n.Replace(hws, "<ws>")))
                .IsEquivalentTo(viaSurface.Next.Select(n => n.Replace(dws, "<ws>")));
            await Assert.That(viaHarness.Data?.ToJsonString()).IsEqualTo(JsonSerializer.SerializeToNode(viaSurface.Data)?.ToJsonString());
        }
    }

    [Test]
    public async Task Harness_StateFields_AreDecoded() {
        var h = PolySurfaceHarness.New();

        var opened = h.Workspace(name: "Orders");
        await Assert.That(opened.Model).IsEqualTo("Orders");
        await Assert.That(opened.Entities).IsEqualTo(0);

        var defined = h.Define(opened.Workspace!, Valid);
        await Assert.That(defined.Success).IsTrue().Because(defined.Summary);
        await Assert.That(defined.Entities).IsEqualTo(1);
        await Assert.That(defined.Instances).IsEqualTo(0);
    }

    [Test]
    public async Task Define_DryRun_OfAValidDocument_DoesNotApply() {
        var h = PolySurfaceHarness.New();
        var ws = h.Open("V2");

        var dry = h.Define(ws, Valid, apply: false);

        await Assert.That(dry.Success).IsTrue().Because(dry.Summary);
        await Assert.That(dry.Summary).StartsWith("Dry run");
        await Assert.That(dry.Revision).IsEqualTo(0L);
        await Assert.That(h.Inspect(ws).Data!["entityCount"]!.GetValue<int>()).IsEqualTo(0);
    }

    [Test]
    public async Task Define_DryRun_OfAnInvalidDocument_ReportsFindingsAndKeepsRevision() {
        var h = PolySurfaceHarness.New();
        var ws = h.Open("V2");
        await Assert.That(h.Define(ws, Valid).Revision).IsEqualTo(1L);

        var dry = h.Define(ws, Invalid, apply: false);

        await Assert.That(dry.Success).IsFalse();
        await Assert.That(dry.Findings.Any(f => f.Code == "DMEFF008")).IsTrue()
            .Because(string.Join("; ", dry.Findings.Select(f => f.Code)));
        await Assert.That(dry.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Define_DryRun_OfAParseError_ReportsAPositionedFinding() {
        var h = PolySurfaceHarness.New();
        var ws = h.Open("V2");

        var dry = h.Define(ws, "domain Broken\nItem: entity {\n  Name Text\n}", apply: false);

        await Assert.That(dry.Success).IsFalse().Because(dry.Summary);
        await Assert.That(dry.Findings).IsNotEmpty().Because(dry.Summary);
        await Assert.That(dry.Findings.Any(f => f.Line is not null)).IsTrue()
            .Because(string.Join("; ", dry.Findings.Select(f => $"{f.Code}@{f.Line}:{f.Column} {f.Message}")));
        await Assert.That(dry.Revision).IsEqualTo(0L);
    }

    [Test]
    public async Task Check_Expression_EvaluatesAgainstBindings() {
        var h = PolySurfaceHarness.New();
        var ws = h.Open("V2");
        await Assert.That(h.Define(ws, Valid).Success).IsTrue();

        var check = h.Check(ws, expression: "Total > 100", bindings: """{"Total":150}""");

        await Assert.That(check.Success).IsTrue().Because(check.Summary);
        await Assert.That(check.Data!["verdict"]!.GetValue<string>()).IsEqualTo("proven");
        await Assert.That(check.Data["result"]!.GetValue<bool>()).IsTrue();
    }

    private const string Orders = """
        domain Stages
        Order: entity {
          Memo: Text
          Draft: stage {
            Cancel: action { transition to Closed }
            Activate: action { transition to Active }
          }
          Active: stage {
            Cancel: action require not Ready { transition to Closed }
          }
          Closed: stage { }
          Ready: policy { Memo is "m" }
        }
        """;

    private const string Families = """
        domain Families
        Parent: entity {
          Name: Text
          kids: many Child
        }
        Child: entity {
          Name: Text
        }
        """;

    private const string Patrons = """
        domain Patrons
        Patron: entity {
          Name: Text
          loans: many Loan
          Active: stage {
            CheckOut: action (amount: Number) {
              create in loans { Amount: amount }
            }
          }
        }
        Loan: entity {
          Amount: Number
          borrower: Patron
        }
        """;

    private static string OpenWith(PolySurfaceHarness h, string name, string poly) {
        var ws = h.Open(name);
        var define = h.Define(ws, poly);
        if (!define.Success)
            throw new InvalidOperationException(define.Summary);
        return ws;
    }

    [Test]
    public async Task Over_SharesTheStore() {
        var (ws, _) = McpTestTools.Store.Create("Q05aShared");
        var h = McpTestTools.Surface;

        var define = h.Define(ws, Valid);

        await Assert.That(define.Success).IsTrue().Because(define.Summary);
        await Assert.That(h.Store).IsSameReferenceAs(McpTestTools.Store);
        await Assert.That(McpTestTools.Store.TryGet(ws, out var state)).IsTrue();
        await Assert.That(state.Revision).IsEqualTo(define.Revision!.Value);
        await Assert.That(state.Revision).IsGreaterThan(0L);
    }

    [Test]
    public async Task Over_TwoHarnessesOnOneStore_SeeEachOthersWrites() {
        var store = McpTestTools.Store;
        var first = PolySurfaceHarness.Over(store);
        var second = PolySurfaceHarness.Over(store);
        var ws = OpenWith(first, "Q05aTwo", Orders);

        var seen = second.Inspect(ws);
        var created = second.Create(ws, "Order", """{"Memo":"m"}""");
        var fetched = first.Get(ws, created.InstanceId);

        await Assert.That(seen.Success).IsTrue().Because(seen.Summary);
        await Assert.That(seen.Entities).IsEqualTo(1);
        await Assert.That(created.Ok).IsTrue().Because(created.Summary);
        await Assert.That(fetched.Ok).IsTrue().Because(fetched.Summary);
        await Assert.That(fetched.InstanceId).IsEqualTo(created.InstanceId);
    }

    [Test]
    public async Task Step_Accessors_ReadSimulateTrace() {
        var h = PolySurfaceHarness.New();
        var ws = OpenWith(h, "Trace", Orders);

        var run = h.Simulate(
            ws,
            SimStep.Create("Order", """{"Memo":"m"}""", alias: "o"),
            SimStep.Invoke("o", "Activate"),
            SimStep.Invoke("o", "Cancel"),
            SimStep.Get("o"));

        await Assert.That(run.Success).IsTrue().Because(run.Summary);
        await Assert.That(run.Steps.Count).IsEqualTo(3).Because("stopOnError ends the trace at the blocked step");
        await Assert.That(run.Step(0).Op).IsEqualTo("create");
        await Assert.That(run.Step(0).Ok).IsTrue().Because(run.Step(0).Summary);
        await Assert.That(run.Step(0).InstanceId).IsNotEmpty();
        await Assert.That(run.Step(0).Property("Memo")).IsEqualTo("m");
        await Assert.That(run.Step(1).Result.Succeeded).IsTrue();
        await Assert.That(run.Step(1).Result.NewStage).IsEqualTo("Active");
        await Assert.That(run.Step(2).Ok).IsFalse();
        await Assert.That(run.Step(2).Result.Succeeded).IsFalse();
        await Assert.That(string.Join(",", run.Step(2).Result.FailedGuards)).IsEqualTo("not_Ready");
        await Assert.That(run.Step(2).Summary).IsNotEmpty();
    }

    [Test]
    public async Task Simulate_StopOnErrorFalse_ContinuesPastAFailedStep() {
        var h = PolySurfaceHarness.New();
        var ws = OpenWith(h, "Continue", Orders);
        SimStep[] steps = [
            SimStep.Create("Order", """{"Memo":"m"}""", alias: "o"),
            SimStep.Invoke("o", "NoSuchAction"),
            SimStep.Get("o"),
        ];

        var stopped = h.Simulate(ws, steps, reset: true);
        var continued = h.Simulate(ws, steps, reset: true, stopOnError: false);

        await Assert.That(stopped.Steps.Count).IsEqualTo(2);
        await Assert.That(stopped.Step(1).Ok).IsFalse();
        await Assert.That(continued.Success).IsTrue().Because(continued.Summary);
        await Assert.That(continued.Steps.Count).IsEqualTo(3);
        await Assert.That(continued.Step(1).Ok).IsFalse();
        await Assert.That(continued.Step(2).Ok).IsTrue().Because(continued.Step(2).Summary);
    }

    [Test]
    public async Task Create_Shorthand_EmbedsPropertiesAsJsonNotAsAString() {
        var h = PolySurfaceHarness.New();
        var ws = OpenWith(h, "Props", SellableShape);

        var created = h.Create(ws, "Item", """{"Name":"Widget","Qty":3}""");

        await Assert.That(created.Ok).IsTrue().Because(created.Summary);
        await Assert.That(created.Op).IsEqualTo("create");
        await Assert.That(created.Property("Qty")).IsEqualTo("3");
        await Assert.That(created.Property("Name")).IsEqualTo("Widget");
        var missing = Thrown(() => created.Property("Missing"));
        await Assert.That(missing).IsNotNull();
        await Assert.That(missing!.Message).Contains("Missing").And.Contains("create");
    }

    [Test]
    public async Task Create_WithoutProperties_RunsTheStepWithNone() {
        var h = PolySurfaceHarness.New();
        var ws = OpenWith(h, "NoProps", Families);

        var created = h.Create(ws, "Parent");

        await Assert.That(created.Ok).IsFalse().Because("Name is required at create");
        await Assert.That(created.Summary).IsNotEmpty();
    }

    [Test]
    public async Task Shorthands_ChainALiteralInstanceId_AcrossSeparateCalls() {
        var h = PolySurfaceHarness.New();
        var ws = OpenWith(h, "Chain", Orders);

        var created = h.Create(ws, "Order", """{"Memo":"m"}""");
        var activated = h.Invoke(ws, created.InstanceId, "Activate");
        var blocked = h.Invoke(ws, created.InstanceId, "Cancel");
        var fetched = h.Get(ws, created.InstanceId);
        var listed = h.List(ws, "Order");
        var evaluated = h.Evaluate(ws, "Order", "Ready", created.InstanceId);

        await Assert.That(activated.Op).IsEqualTo("invoke");
        await Assert.That(activated.Ok).IsTrue().Because(activated.Summary);
        await Assert.That(activated.Result.NewStage).IsEqualTo("Active");
        await Assert.That(blocked.Ok).IsFalse();
        await Assert.That(string.Join(",", blocked.Result.FailedGuards)).IsEqualTo("not_Ready");
        await Assert.That(fetched.Op).IsEqualTo("get");
        await Assert.That(fetched.Data!["instance"]!["currentStage"]!.GetValue<string>()).IsEqualTo("Active");
        await Assert.That(listed.Op).IsEqualTo("list");
        await Assert.That(listed.Data!["count"]!.GetValue<int>()).IsEqualTo(1);
        await Assert.That(evaluated.Op).IsEqualTo("evaluate");
        await Assert.That(evaluated.Ok).IsTrue().Because(evaluated.Summary);
    }

    [Test]
    public async Task Invoke_Shorthand_EmbedsArgsAsJsonNotAsAString() {
        var h = PolySurfaceHarness.New();
        var ws = OpenWith(h, "Args", Patrons);
        var patron = h.Create(ws, "Patron", """{"Name":"Alice"}""");

        var checkOut = h.Invoke(ws, patron.InstanceId, "CheckOut", """{"amount":100}""");
        var loans = h.List(ws, "Loan");

        await Assert.That(checkOut.Ok).IsTrue().Because(checkOut.Summary);
        await Assert.That(checkOut.Result.Succeeded).IsTrue();
        await Assert.That(loans.Data!["count"]!.GetValue<int>()).IsEqualTo(1);
    }

    [Test]
    public async Task Link_Unlink_Shorthands_RunOneStepEach() {
        var h = PolySurfaceHarness.New();
        var ws = OpenWith(h, "Links", Families);
        var parent = h.Create(ws, "Parent", """{"Name":"p"}""");
        var child = h.Create(ws, "Child", """{"Name":"c"}""");

        var linked = h.Link(ws, parent.InstanceId, "kids", child.InstanceId);
        var unlinked = h.Unlink(ws, parent.InstanceId, "kids", child.InstanceId);

        await Assert.That(linked.Op).IsEqualTo("link");
        await Assert.That(linked.Ok).IsTrue().Because(linked.Summary);
        await Assert.That(unlinked.Op).IsEqualTo("unlink");
        await Assert.That(unlinked.Ok).IsTrue().Because(unlinked.Summary);
    }

    [Test]
    public async Task Step_WhenTheResponseHasNoTrace_ThrowsADescriptiveError() {
        var h = PolySurfaceHarness.New();

        var noWorkspace = h.Simulate("", SimStep.List());
        var badJson = h.Simulate(h.Open("Bad"), "not json");

        foreach (var (result, fragment) in new[] { (noWorkspace, "WORKSPACE_REQUIRED"), (badJson, "Invalid steps JSON") }) {
            Exception? thrown = null;
            try {
                _ = result.Step(0);
            }
            catch (Exception ex) {
                thrown = ex;
            }

            await Assert.That(thrown).IsNotNull();
            await Assert.That(thrown!.GetType()).IsEqualTo(typeof(InvalidOperationException));
            await Assert.That(thrown.Message).Contains(fragment);
        }
    }

    [Test]
    public async Task Step_IndexOutOfRange_ThrowsADescriptiveError() {
        var h = PolySurfaceHarness.New();
        var ws = OpenWith(h, "Range", Orders);
        var run = h.Simulate(ws, SimStep.List());

        Exception? thrown = null;
        try {
            _ = run.Step(5);
        }
        catch (Exception ex) {
            thrown = ex;
        }

        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.GetType()).IsEqualTo(typeof(InvalidOperationException));
        await Assert.That(thrown.Message).Contains("5");
    }

    [Test]
    public async Task InvokeOutcome_ReadsBothShapes_AndAStepWithNoDataThrows() {
        var thrown = new HarnessStep(0, "invoke", false, "Action execution failed: boom",
            JsonNode.Parse("""{"actionName":"A","error":"boom"}"""));
        var noData = new HarnessStep(1, "invoke", false, "Step 1 failed: nope", null);
        var succeeded = new HarnessStep(2, "invoke", true, "ok",
            JsonNode.Parse("""{"invokeActionResult":{"actionName":"A","succeeded":true,"newStage":"Active","failedGuards":null,"errorMessage":null}}"""));

        await Assert.That(thrown.Result.Succeeded).IsFalse();
        await Assert.That(thrown.Result.ErrorMessage).IsEqualTo("boom");
        await Assert.That(thrown.Result.FailedGuards).IsEmpty();
        var noDataThrown = Thrown(() => noData.Result);
        await Assert.That(noDataThrown).IsNotNull();
        await Assert.That(noDataThrown!.Message).Contains("Step 1").And.Contains("invoke").And.Contains("Step 1 failed: nope");
        await Assert.That(succeeded.Result.Succeeded).IsTrue();
        await Assert.That(succeeded.Result.NewStage).IsEqualTo("Active");
        await Assert.That(succeeded.Result.ErrorMessage).IsNull();
    }

    [Test]
    public async Task Result_OfAnInvokeOnAnUnknownInstance_ThrowsInsteadOfInventingAnOutcome() {
        var h = PolySurfaceHarness.New();
        var ws = OpenWith(h, "NoInstance", Orders);

        var step = h.Invoke(ws, "no-such-id", "Cancel");
        var thrown = Thrown(() => step.Result);

        await Assert.That(step.Ok).IsFalse();
        await Assert.That(step.Summary).IsNotEmpty();
        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.Message).Contains("invoke").And.Contains(step.Summary);
    }

    [Test]
    public async Task Result_OfANonInvokeStep_Throws() {
        var h = PolySurfaceHarness.New();
        var ws = OpenWith(h, "NonInvoke", Orders);
        var created = h.Create(ws, "Order", """{"Memo":"m"}""");

        var thrown = Thrown(() => h.Get(ws, created.InstanceId).Result);

        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.Message).Contains("get");
    }

    [Test]
    public async Task Property_ComparedAcrossTwoCreates_ThrowsWhenNeitherHasIt() {
        var h = PolySurfaceHarness.New();
        var ws = OpenWith(h, "TwoCreates", Orders);
        var a = h.Create(ws, "Order", """{"Memo":"m"}""");
        var b = h.Create(ws, "Order", """{"Memo":"m"}""");

        var thrown = Thrown(() => a.Property("Nope") == b.Property("Nope"));

        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.Message).Contains("Nope");
    }

    [Test]
    public async Task Property_OfAFailedStepOrAStepWithNoInstance_Throws() {
        var h = PolySurfaceHarness.New();
        var ws = OpenWith(h, "NoProps", Orders);
        var failed = h.Get(ws, "no-such-id");
        var listed = h.List(ws);

        await Assert.That(failed.Ok).IsFalse();
        await Assert.That(Thrown(() => failed.Property("Memo"))).IsNotNull();
        await Assert.That(Thrown(() => listed.Property("Memo"))).IsNotNull();
    }

    [Test]
    public async Task Result_OfAnActionThatReturnsAnEntity_CarriesTheReturnShape() {
        var h = PolySurfaceHarness.New();
        var ws = OpenWith(h, "Tickets", """
            domain Tickets
            Ticket: entity {
              Memo: Text
              Draft: stage {
                Cancel: action -> Receipt { create Receipt { Note: "done" } }
                Activate: action { transition to Active }
              }
              Active: stage { Cancel: action { transition to Closed } }
              Closed: stage { }
            }
            Receipt: entity { Note: Text }
            """);
        var returning = h.Create(ws, "Ticket", """{"Memo":"m"}""");
        var plain = h.Create(ws, "Ticket", """{"Memo":"m"}""");
        await Assert.That(h.Invoke(ws, plain.InstanceId, "Activate").Ok).IsTrue();

        var fromDraft = h.Invoke(ws, returning.InstanceId, "Cancel").Result;
        var fromActive = h.Invoke(ws, plain.InstanceId, "Cancel").Result;

        await Assert.That(fromDraft.Succeeded).IsTrue();
        await Assert.That(fromDraft.ReturnTypeName).IsEqualTo("Receipt");
        await Assert.That(fromDraft.ReturnInstanceId).IsNotNull();
        await Assert.That(fromActive.ReturnTypeName).IsNull();
        await Assert.That(fromActive.ReturnInstanceId).IsNull();
        await Assert.That(fromActive.NewStage).IsEqualTo("Closed");
    }

    [Test]
    public async Task Steps_WhenTheResponseHasNoTrace_Throws() {
        var h = PolySurfaceHarness.New();

        var thrown = Thrown(() => h.Simulate("", SimStep.List()).Steps);

        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.Message).Contains("WORKSPACE_REQUIRED");
    }

    [Test]
    public async Task Steps_OfAMalformedTraceEntry_Throws() {
        var malformed = HarnessResult.FromEnvelope(JsonNode.Parse(
            """{"ok":true,"summary":"s","data":{"trace":[{"index":0,"op":"get","summary":"x","data":null}]}}""")!);

        var thrown = Thrown(() => malformed.Steps);

        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.Message).Contains("ok");
    }

    private static Exception? Thrown(Func<object?> action) {
        try {
            _ = action();
            return null;
        }
        catch (Exception ex) {
            return ex;
        }
    }

    [Test]
    public async Task Content_OfAFailedExport_ThrowsWithTheCodeAndSummary() {
        var h = PolySurfaceHarness.New();
        var ws = OpenWith(h, "Failed", Orders);

        var unknownFormat = h.Export(ws, "no-such-format");
        var unknownWorkspace = h.Export("no-such-workspace", "dsl");
        var formatThrown = Thrown(() => unknownFormat.Content);
        var workspaceThrown = Thrown(() => unknownWorkspace.Content);

        await Assert.That(unknownFormat.Success).IsFalse();
        await Assert.That(unknownFormat.Data).IsNull();
        await Assert.That(formatThrown).IsNotNull();
        await Assert.That(formatThrown!.Message).Contains(unknownFormat.Summary);
        await Assert.That(workspaceThrown).IsNotNull();
        await Assert.That(workspaceThrown!.Message).Contains("WORKSPACE_REQUIRED");
    }

    [Test]
    public async Task Content_TwoFailedExportsCompared_ThrowInsteadOfComparingEqual() {
        var h = PolySurfaceHarness.New();
        var ws = OpenWith(h, "FixedPoint", Orders);

        var thrown = Thrown(() => h.Export(ws, "no-such-format").Content == h.Export(ws, "no-such-format").Content);

        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.GetType()).IsEqualTo(typeof(InvalidOperationException));
    }
}