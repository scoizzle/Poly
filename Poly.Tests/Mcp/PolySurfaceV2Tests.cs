using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol.Server;

using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.Mcp.Surface;

namespace Poly.Tests.Mcp;

/// <summary>
/// Contract tests for the v2 surface: seven outcome tools, one envelope, explicit
/// workspace handle, and the author → prove → export/simulate loop.
/// </summary>
public class PolySurfaceV2Tests {
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

    private static PolySurfaceTools Surface() =>
        new(McpTestTools.Store, NullLogger<PolySurfaceTools>.Instance);

    private static string Open(PolySurfaceTools surface, string name = "V2") {
        var response = surface.Workspace(name: name);
        return response.State!.Workspace;
    }

    [Test]
    public async Task Open_CreatesWorkspaceWithState() {
        var surface = Surface();
        var response = surface.Workspace(name: "Orders");

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(response.State).IsNotNull();
        await Assert.That(response.State!.Model).IsEqualTo("Orders");
        await Assert.That(response.State.Revision).IsEqualTo(0);
        await Assert.That(response.Next.Any(n => n.StartsWith("poly_define", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Open_ListReturnsHandles() {
        var surface = Surface();
        var created = surface.Workspace(name: "Listed");

        var listed = surface.Workspace(op: "list");

        await Assert.That(listed.Ok).IsTrue();
        await Assert.That(listed.Data).IsNotNull();
        await Assert.That(created.State).IsNotNull();
    }

    [Test]
    public async Task Guide_DefaultIsShortAndTopicDelegates() {
        var surface = Surface();

        var quickstart = surface.Guide();
        var section = surface.Guide("3");

        await Assert.That(quickstart.Ok).IsTrue();
        var guide = JsonSerializer.SerializeToNode(quickstart.Data)!["guide"]!.GetValue<string>();
        await Assert.That(guide.Length).IsLessThan(2000);
        await Assert.That(guide).Contains("poly_define");
        await Assert.That(section.Ok).IsTrue();
        await Assert.That(section.Data).IsNotNull();
    }

    [Test]
    public async Task Define_ValidDocumentAppliesAndBumpsRevision() {
        var surface = Surface();
        var workspace = Open(surface);

        var response = surface.Define(workspace, Valid);

        await Assert.That(response.Ok).IsTrue();
        await Assert.That(response.State!.Revision).IsEqualTo(1);
        await Assert.That(response.State.Entities).IsEqualTo(1);
        await Assert.That(response.Next.Any(n => n.StartsWith("poly_check", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Define_RejectedDocumentLeavesRevisionAndReportsFindings() {
        var surface = Surface();
        var workspace = Open(surface);
        await Assert.That(surface.Define(workspace, Valid).Ok).IsTrue();
        var revision = surface.Inspect(workspace).State!.Revision;

        var response = surface.Define(workspace, Invalid);

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.State!.Revision).IsEqualTo(revision);
        await Assert.That(response.Findings.Any(f => f.Code == "DMEFF008")).IsTrue();
        await Assert.That(response.Next.Any(n => n.StartsWith("poly_guide", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Define_DryRunDoesNotApply() {
        var surface = Surface();
        var workspace = Open(surface);

        var dry = surface.Define(workspace, Valid, apply: false);
        var overview = surface.Inspect(workspace);

        await Assert.That(dry.Ok).IsTrue();
        await Assert.That(dry.Summary).StartsWith("Dry run");
        await Assert.That(dry.State!.Revision).IsEqualTo(0);
        await Assert.That(JsonSerializer.SerializeToNode(overview.Data)!["entityCount"]!.GetValue<int>())
            .IsEqualTo(0);
    }

    [Test]
    public async Task Check_ProvenForAValidModel() {
        var surface = Surface();
        var workspace = Open(surface);
        await Assert.That(surface.Define(workspace, Valid).Ok).IsTrue();

        var response = surface.Check(workspace);

        await Assert.That(response.Ok).IsTrue();
        var data = JsonSerializer.SerializeToNode(response.Data)!;
        await Assert.That(data["verdict"]!.GetValue<string>()).IsEqualTo("proven");
        await Assert.That(data["errorCount"]!.GetValue<int>()).IsEqualTo(0);
    }

    [Test]
    public async Task Check_ViolatedWhenTheModelHasErrors() {
        var (sessionId, state) = McpTestTools.Store.Create("Broken");
        var broken = new Domain("Broken", [
            new Entity(
                "Item",
                [new Property("Missing", new DomainTypeReference("NoSuchType"), [])],
                [], [], [])
        ]);
        var analysis = DomainModelAnalyzer.Analyze(broken);
        await Assert.That(analysis.HasErrors).IsTrue();
        McpTestTools.Store.Replace(sessionId, broken, analysis, state.Modeling.WithDomain(broken));

        var response = Surface().Check(sessionId);

        await Assert.That(response.Ok).IsTrue();
        var data = JsonSerializer.SerializeToNode(response.Data)!;
        await Assert.That(data["verdict"]!.GetValue<string>()).IsEqualTo("violated");
        await Assert.That(response.Findings.Any(f => f.Severity == "Error")).IsTrue();
    }

    [Test]
    public async Task Check_UnknownWhenArgumentSpecificProofIsRequested() {
        var surface = Surface();
        var workspace = Open(surface);
        await Assert.That(surface.Define(workspace, Valid).Ok).IsTrue();

        var response = surface.Check(workspace, select: "Product.Submit", args: """{"delta":500}""");

        var data = JsonSerializer.SerializeToNode(response.Data)!;
        await Assert.That(data["verdict"]!.GetValue<string>()).IsEqualTo("unknown");
    }

    [Test]
    public async Task Inspect_OverviewEntityAndMember() {
        var surface = Surface();
        var workspace = Open(surface);
        await Assert.That(surface.Define(workspace, Valid).Ok).IsTrue();

        var overview = surface.Inspect(workspace);
        var entity = surface.Inspect(workspace, "Product", detail: "full");
        var action = surface.Inspect(workspace, "Product.Submit", detail: "full");

        await Assert.That(JsonSerializer.SerializeToNode(overview.Data)!["entityNames"]!
            .AsArray().Select(n => n!.GetValue<string>())).Contains("Product");
        await Assert.That(entity.Ok).IsTrue();
        await Assert.That(action.Ok).IsTrue();
        await Assert.That(action.Summary).Contains("Submit");
    }

    [Test]
    public async Task Export_InlineJsonSchemaAndLargeFilePath() {
        var surface = Surface();
        var workspace = Open(surface);
        await Assert.That(surface.Define(workspace, Valid).Ok).IsTrue();

        var schema = surface.Export(workspace, "json-schema");
        var schemaData = JsonSerializer.SerializeToNode(schema.Data)!;
        await Assert.That(schema.Ok).IsTrue();
        await Assert.That(schemaData["content"]!.GetValue<string>()).Contains("\"$schema\"");

        var csharp = surface.Export(workspace, "csharp");
        var csharpData = JsonSerializer.SerializeToNode(csharp.Data)!;
        await Assert.That(csharp.Ok).IsTrue();
        await Assert.That(csharpData["content"] is not null || csharpData["path"] is not null).IsTrue();
        if (csharpData["path"] is JsonValue path) {
            await Assert.That(File.Exists(path.GetValue<string>())).IsTrue();
        }
    }

    [Test]
    public async Task Simulate_CreateInvokeAndGetWithAlias() {
        var surface = Surface();
        var workspace = Open(surface);
        await Assert.That(surface.Define(workspace, Valid).Ok).IsTrue();

        var steps = """
            [
              {"op":"create","entity":"Product","properties":{"Total":42},"as":"p1"},
              {"op":"invoke","instanceId":"p1","action":"Submit"},
              {"op":"get","instanceId":"p1"}
            ]
            """;
        var response = surface.Simulate(workspace, steps);

        var data = JsonSerializer.SerializeToNode(response.Data)!;
        await Assert.That(response.Ok).IsTrue();
        await Assert.That(data["trace"]!.AsArray().Count).IsEqualTo(3);
        await Assert.That(data["failedAt"]).IsNull();
        await Assert.That(response.State!.Instances).IsEqualTo(1);
        await Assert.That(data["trace"]!.AsArray()[2]!["data"]!.ToJsonString()).Contains("Active");
    }

    [Test]
    public async Task Check_SelectMemberPath_IsNotProvenOnAViolatedModel() {
        var (sessionId, state) = McpTestTools.Store.Create("Broken");
        var broken = new Domain("Broken", [
            new Entity(
                "Item",
                [new Property("Missing", new DomainTypeReference("NoSuchType"), [])],
                [], [], [])
        ]);
        var analysis = DomainModelAnalyzer.Analyze(broken);
        await Assert.That(analysis.HasErrors).IsTrue();
        McpTestTools.Store.Replace(sessionId, broken, analysis, state.Modeling.WithDomain(broken));

        var surface = Surface();
        var selected = surface.Check(sessionId, select: "Item.Missing");
        var entity = surface.Check(sessionId, select: "Item");
        var missing = surface.Check(sessionId, select: "Item.Nope");

        var selectedData = JsonSerializer.SerializeToNode(selected.Data)!;
        var entityData = JsonSerializer.SerializeToNode(entity.Data)!;
        var missingData = JsonSerializer.SerializeToNode(missing.Data)!;
        await Assert.That(selectedData["verdict"]!.GetValue<string>()).IsEqualTo("violated");
        await Assert.That(entityData["verdict"]!.GetValue<string>()).IsEqualTo("violated")
            .Because("selecting an entity must include its members' findings");
        await Assert.That(missingData["verdict"]!.GetValue<string>()).IsEqualTo("unknown");
    }

    [Test]
    public async Task Simulate_RejectedInputDoesNotResetInstances() {
        var surface = Surface();
        var workspace = Open(surface);
        await Assert.That(surface.Define(workspace, Valid).Ok).IsTrue();
        var created = surface.Simulate(workspace,
            """[{"op":"create","entity":"Product","properties":{"Total":1}}]""");
        await Assert.That(created.State!.Instances).IsEqualTo(1);
        var revision = created.State.Revision;

        var rejected = surface.Simulate(workspace, "not json", reset: true);

        await Assert.That(rejected.Ok).IsFalse();
        await Assert.That(rejected.State!.Instances).IsEqualTo(1);
        await Assert.That(rejected.State.Revision).IsEqualTo(revision);
    }

    [Test]
    public async Task Simulate_NonObjectStepKeepsTheEnvelope() {
        var surface = Surface();
        var workspace = Open(surface);
        await Assert.That(surface.Define(workspace, Valid).Ok).IsTrue();

        var response = surface.Simulate(workspace, """["create"]""");

        await Assert.That(response.Ok).IsTrue();
        var data = JsonSerializer.SerializeToNode(response.Data)!;
        await Assert.That(data["failedAt"]!.GetValue<int>()).IsEqualTo(0);
    }

    [Test]
    public async Task Export_EveryFormatReturnsNonEmptyContent() {
        var surface = Surface();
        var workspace = Open(surface);
        await Assert.That(surface.Define(workspace, Valid).Ok).IsTrue();

        foreach (var format in new[] { "dsl", "json-schema", "csharp", "markdown", "html", "json-ld", "shacl" }) {
            var response = surface.Export(workspace, format);
            var data = JsonSerializer.SerializeToNode(response.Data)!;
            await Assert.That(response.Ok).IsTrue();
            var content = data["content"]?.GetValue<string>();
            var path = data["path"]?.GetValue<string>();
            await Assert.That(!string.IsNullOrWhiteSpace(content) || !string.IsNullOrWhiteSpace(path)).IsTrue();
        }
    }

    [Test]
    public async Task Open_ListIncludesTheCreatedWorkspace() {
        var surface = Surface();
        var created = surface.Workspace(name: "Listed");
        var listed = surface.Workspace(op: "list");
        var data = JsonSerializer.SerializeToNode(listed.Data)!;

        await Assert.That(data["sessions"]!.AsArray()
            .Select(s => s!["workspace"]!.GetValue<string>())).Contains(created.State!.Workspace);
    }

    [Test]
    public async Task Simulate_LinkEvaluateUnlinkAndList() {
        var surface = Surface();
        var workspace = Open(surface);
        var defined = surface.Define(workspace, """
            domain Sim
            Line: entity { Sku: Text }
            Order: entity {
              Total: Number
              CanShip: policy { Total > 0 }
              lines: many Line
            }
            """);
        await Assert.That(defined.Ok).IsTrue();

        var response = surface.Simulate(workspace, """
            [
              {"op":"create","entity":"Order","properties":{"Total":10},"as":"o"},
              {"op":"create","entity":"Line","properties":{"Sku":"A"},"as":"l"},
              {"op":"link","sourceInstanceId":"o","relationship":"lines","targetInstanceId":"l"},
              {"op":"evaluate","entity":"Order","policy":"CanShip","instanceId":"o"},
              {"op":"get","instanceId":"o"},
              {"op":"list","entity":"Line"},
              {"op":"unlink","sourceInstanceId":"o","relationship":"lines","targetInstanceId":"l"},
              {"op":"list","entity":"Line"}
            ]
            """);

        var data = JsonSerializer.SerializeToNode(response.Data)!;
        await Assert.That(response.Ok).IsTrue();
        await Assert.That(data["failedAt"]).IsNull();
        await Assert.That(data["trace"]!.AsArray().Count).IsEqualTo(8);
        await Assert.That(data["trace"]![3]!["data"]!["result"]!.GetValue<bool>()).IsTrue()
            .Because("the CanShip policy evaluates on the linked Order instance");
        await Assert.That(data["trace"]![4]!["data"]!["instance"]!["navigationLinks"]!.AsArray().Count)
            .IsGreaterThan(0);
        await Assert.That(data["trace"]![5]!["data"]!["count"]!.GetValue<int>()).IsEqualTo(1);
    }

    [Test]
    public async Task Simulate_GuardBlockedStepAndReset() {
        var surface = Surface();
        var workspace = Open(surface);
        var defined = surface.Define(workspace, """
            domain Guard
            Task: entity {
              Locked: Boolean
              Attempts: Number
              CanRun: policy { Locked == false }
              Run: action require CanRun { assign Attempts to Attempts + 1 }
            }
            """);
        await Assert.That(defined.Ok).IsTrue();

        var stopped = surface.Simulate(workspace, """
            [
              {"op":"create","entity":"Task","properties":{"Locked":true,"Attempts":0},"as":"t"},
              {"op":"invoke","instanceId":"t","action":"Run"},
              {"op":"get","instanceId":"t"}
            ]
            """);
        var stoppedData = JsonSerializer.SerializeToNode(stopped.Data)!;
        await Assert.That(stopped.Ok).IsTrue();
        await Assert.That(stoppedData["trace"]!.AsArray().Count).IsEqualTo(2)
            .Because("the guard-blocked invoke is a failed step and stopOnError defaults true");
        await Assert.That(stoppedData["failedAt"]!.GetValue<int>()).IsEqualTo(1);

        var reset = surface.Simulate(workspace, """[{"op":"list","entity":"Task"}]""", reset: true);
        var resetData = JsonSerializer.SerializeToNode(reset.Data)!;
        await Assert.That(resetData["trace"]![0]!["data"]!["count"]!.GetValue<int>()).IsEqualTo(0)
            .Because("reset clears instances before the steps run");

        var continued = surface.Simulate(workspace, """
            [
              {"op":"create","entity":"Task","properties":{"Locked":false,"Attempts":0},"as":"t"},
              {"op":"invoke","instanceId":"t","action":"Run"},
              {"op":"get","instanceId":"t"}
            ]
            """, stopOnError: false);
        var continuedData = JsonSerializer.SerializeToNode(continued.Data)!;
        await Assert.That(continuedData["trace"]!.AsArray().Count).IsEqualTo(3);
        await Assert.That(continuedData["failedAt"]).IsNull();
    }

    [Test]
    public async Task Check_ExpressionReturnsAVerdict() {
        var surface = Surface();
        var workspace = Open(surface);
        await Assert.That(surface.Define(workspace, Valid).Ok).IsTrue();

        var truthy = surface.Check(workspace, expression: "Total > 100", bindings: """{"Total":150}""");
        var falsy = surface.Check(workspace, expression: "Total > 100", bindings: """{"Total":50}""");

        await Assert.That(JsonSerializer.SerializeToNode(truthy.Data)!["verdict"]!.GetValue<string>())
            .IsEqualTo("proven");
        await Assert.That(JsonSerializer.SerializeToNode(falsy.Data)!["verdict"]!.GetValue<string>())
            .IsEqualTo("violated");
    }

    [Test]
    public async Task Define_DryRunInvalid_LeavesRevision() {
        var surface = Surface();
        var workspace = Open(surface);
        await Assert.That(surface.Define(workspace, Valid).Ok).IsTrue();
        var revision = surface.Inspect(workspace).State!.Revision;

        var dry = surface.Define(workspace, Invalid, apply: false);

        await Assert.That(dry.Ok).IsFalse();
        await Assert.That(dry.State!.Revision).IsEqualTo(revision);
    }

    [Test]
    public async Task FilterAllowedArguments_MatchTheToolParameters() {
        var tools = typeof(PolySurfaceTools).GetMethods()
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(pair => pair.Attribute?.Name is not null)
            .ToList();
        await Assert.That(tools.Count).IsEqualTo(7);

        foreach (var (method, attribute) in tools) {
            var parameters = method.GetParameters()
                .Select(p => p.Name!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            var allowed = UnknownArgumentFilter.AllowedArguments[attribute!.Name!]
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            await Assert.That(string.Join(",", allowed)).IsEqualTo(string.Join(",", parameters))
                .Because($"{attribute.Name} and the argument filter must not drift");
        }
    }

    [Test]
    public async Task Open_CloseRemovesTheWorkspace() {
        var surface = Surface();
        var workspace = Open(surface, "Closable");

        var closed = surface.Workspace(op: "close", workspace: workspace);
        var after = surface.Inspect(workspace);
        var listed = surface.Workspace(op: "list");
        var sessions = JsonSerializer.SerializeToNode(listed.Data)!["sessions"]!.AsArray()
            .Select(s => s!["workspace"]!.GetValue<string>())
            .ToList();

        await Assert.That(closed.Ok).IsTrue();
        await Assert.That(closed.State).IsNull();
        await Assert.That(closed.Summary).Contains("closed");
        await Assert.That(after.Ok).IsFalse();
        await Assert.That(after.ErrorCode).IsEqualTo("WORKSPACE_REQUIRED");
        await Assert.That(sessions).DoesNotContain(workspace);
    }

    [Test]
    public async Task Open_CloseUnknownWorkspaceFailsCleanly() {
        var response = Surface().Workspace(op: "close", workspace: "missing");

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.ErrorCode).IsEqualTo("WORKSPACE_NOT_FOUND");
        await Assert.That(response.Next).Contains("poly_workspace");
    }

    [Test]
    public async Task Open_UnknownOpFailsCleanly() {
        var response = Surface().Workspace(op: "destroy");

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.ErrorCode).IsEqualTo("UNKNOWN_OP");
        await Assert.That(response.Summary).Contains("create, list, or close");
    }

    [Test]
    public async Task UnknownWorkspace_ReturnsWorkspaceRequired() {
        var response = Surface().Define("missing", Valid);

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.ErrorCode).IsEqualTo("WORKSPACE_REQUIRED");
        await Assert.That(response.Next).Contains("poly_workspace");
    }

    /// <summary>
    /// The real transport, not the in-process tool objects: start the built server, read
    /// the initialize instructions, and confirm the advertised catalog. The default (unset)
    /// must be v2; the v1 flag must expose 30 tools without v2 instructions.
    /// </summary>
    [Test]
    public async Task StdioSurface_DefaultAdvertisesSevenToolsAndInstructions() {
        var (instructions, tools) = await ReadSurfaceAsync();
        var names = tools.Select(t => t["name"]!.GetValue<string>())
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        await Assert.That(instructions).Contains("describe the domain you are working in");
        await Assert.That(string.Join(",", names)).IsEqualTo(
            "poly_check,poly_define,poly_export,poly_guide,poly_inspect,poly_simulate,poly_workspace");
        foreach (var tool in tools) {
            await Assert.That(tool["annotations"]).IsNotNull();
            await Assert.That(tool["outputSchema"]).IsNotNull();
            await Assert.That(tool["annotations"]!["title"]).IsNotNull();
        }
    }

    [Test]
    public async Task StdioSurface_RejectsUnknownArgumentsWithValidNames() {
        var root = RepoRoot();
        var dll = Path.Combine(root, "Poly.Mcp/bin/Debug/net10.0/Poly.Mcp.dll");
        var runtimeConfig = Path.Combine(root, "Poly.Mcp/bin/Debug/net10.0/Poly.Mcp.runtimeconfig.json");
        var start = new ProcessStartInfo("dotnet") {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("--runtimeconfig");
        start.ArgumentList.Add(runtimeConfig);
        start.ArgumentList.Add(dll);

        using var process = Process.Start(start)!;
        try {
            async Task SendAsync(Dictionary<string, object?> message) {
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message));
                await process.StandardInput.FlushAsync();
            }
            async Task<JsonNode> ReadUntilAsync(int id) {
                while (true) {
                    var line = await process.StandardOutput.ReadLineAsync()
                        .WaitAsync(TimeSpan.FromSeconds(15));
                    if (line is null)
                        throw new InvalidOperationException("server closed stdout");
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    var node = JsonNode.Parse(line)!;
                    if (node["id"]?.GetValue<int>() == id)
                        return node;
                }
            }

            await SendAsync(new() {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "initialize",
                ["params"] = new {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "test", version = "1" },
                },
            });
            await ReadUntilAsync(1);
            await SendAsync(new() { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" });
            await SendAsync(new() {
                ["jsonrpc"] = "2.0",
                ["id"] = 2,
                ["method"] = "tools/call",
                ["params"] = new {
                    name = "poly_guide",
                    arguments = new Dictionary<string, object?> { ["bogus"] = 1 },
                },
            });
            var response = await ReadUntilAsync(2);
            var text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();

            await Assert.That(response["result"]!["isError"]!.GetValue<bool>()).IsTrue();
            await Assert.That(text).Contains("UNKNOWN_ARGUMENT");
            await Assert.That(text).Contains("Valid: topic");
        }
        finally {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    private static async Task<(string? Instructions, List<JsonObject> Tools)> ReadSurfaceAsync() {
        var root = RepoRoot();
        var dll = Path.Combine(root, "Poly.Mcp/bin/Debug/net10.0/Poly.Mcp.dll");
        var runtimeConfig = Path.Combine(root, "Poly.Mcp/bin/Debug/net10.0/Poly.Mcp.runtimeconfig.json");
        await Assert.That(File.Exists(dll)).IsTrue();
        await Assert.That(File.Exists(runtimeConfig)).IsTrue();

        var start = new ProcessStartInfo("dotnet") {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("--runtimeconfig");
        start.ArgumentList.Add(runtimeConfig);
        start.ArgumentList.Add(dll);

        using var process = Process.Start(start)!;
        try {
            async Task SendAsync(Dictionary<string, object?> message) {
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message));
                await process.StandardInput.FlushAsync();
            }

            async Task<JsonNode> ReadUntilAsync(int id) {
                while (true) {
                    var line = await process.StandardOutput.ReadLineAsync()
                        .WaitAsync(TimeSpan.FromSeconds(15));
                    if (line is null)
                        throw new InvalidOperationException("server closed stdout");
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    var node = JsonNode.Parse(line)!;
                    if (node["id"]?.GetValue<int>() == id)
                        return node;
                }
            }

            await SendAsync(new() {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "initialize",
                ["params"] = new {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "test", version = "1" },
                },
            });
            var initialized = await ReadUntilAsync(1);
            var instructions = initialized["result"]?["instructions"]?.GetValue<string>();

            await SendAsync(new() { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" });
            await SendAsync(new() {
                ["jsonrpc"] = "2.0",
                ["id"] = 2,
                ["method"] = "tools/list",
                ["params"] = new { },
            });
            var listed = await ReadUntilAsync(2);
            var tools = listed["result"]!["tools"]!.AsArray()
                .Select(t => t!.AsObject())
                .ToList();
            return (instructions, tools);
        }
        finally {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    private static string RepoRoot() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Poly.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Could not find the repository root from the test output.");
    }
}