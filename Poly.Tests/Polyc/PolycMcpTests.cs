using System.Text.Json;
using System.Text.Json.Nodes;

using Poly.Compile;
using Poly.Mcp;
using Poly.Packs.Product;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Polyc;

/// <summary>Scripted stdio client against the one host, <c>polyc mcp</c>. Started through
/// <see cref="PolycProcess.McpAsync"/>, so each host holds a gate slot and is killed as a tree on dispose.</summary>
public class PolycMcpTests {
    private static readonly string[] SevenTools = [
        "poly_check", "poly_define", "poly_export", "poly_guide", "poly_inspect", "poly_simulate", "poly_workspace",
    ];

    private static async Task<string[]> ToolNamesAsync(McpClient host) =>
        [.. (await host.ToolsListAsync()).Select(t => t["name"]!.GetValue<string>()).Order(StringComparer.Ordinal)];

    /// <summary>#758: <c>polyc mcp</c> is the only host, so <c>Poly.Mcp</c> is a class library — no entry point,
    /// no forwarder program, no <c>OutputType</c>. The assembly itself stays (the tests and the tools need it).</summary>
    [Test]
    public async Task PolyMcp_HasNoEntryPoint_AndNoProgramFile() {
        var root = PolycTestSupport.RepoRoot();
        await Assert.That(typeof(PolyMcpHost).Assembly.EntryPoint).IsNull();
        await Assert.That(File.Exists(Path.Combine(root, "Poly.Mcp", "Program.cs"))).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(root, "Poly.Mcp", "Poly.Mcp.csproj"))).DoesNotContain("<OutputType>");
    }

    /// <summary>#758: the one MCP client config in the repo launches the host directly. The <c>--</c> is what
    /// forwards <c>mcp</c> to polyc instead of to <c>dotnet run</c>.</summary>
    [Test]
    public async Task OpencodeConfig_LaunchesPolycMcp() {
        var text = File.ReadAllText(Path.Combine(PolycTestSupport.RepoRoot(), "opencode.jsonc"));
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        var command = document.RootElement.GetProperty("mcp").GetProperty("poly-local").GetProperty("command")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        string[] expected = ["dotnet", "run", "--project", "./src/polyc/polyc.csproj", "--", "mcp"];
        await Assert.That(command.SequenceEqual(expected)).IsTrue();
    }

    /// <summary>#758: the restart script rebuilds and kills the polyc host only. <c>polyc</c> is a general CLI, so
    /// the host pattern keeps the <c>polyc/bin</c> anchor plus <c>$</c> and never matches a bare <c>polyc mcp</c>.</summary>
    [Test]
    public async Task RestartScript_TargetsOnlyThePolycHost() {
        var script = File.ReadAllText(Path.Combine(PolycTestSupport.RepoRoot(), "scripts/restart-poly-mcp.sh"));
        await Assert.That(script).Contains("src/polyc/polyc.csproj");
        await Assert.That(script).DoesNotContain("Poly.Mcp");
        await Assert.That(script).Contains("polyc/bin.* mcp$");
        await Assert.That(script).DoesNotContain("polyc mcp$");
    }

    /// <summary>The tool list and the server instructions an agent sees are the ones <c>Poly.Mcp</c> declares,
    /// now that the forwarder test is gone: the pin moves here with the host.</summary>
    [Test]
    public async Task PolycMcp_ListsSevenToolsAndTheHostInstructions() {
        using var host = await PolycProcess.McpAsync(McpTarget.Polyc);
        var initialized = await host.InitializeAsync();
        await Assert.That(initialized["instructions"]!.GetValue<string>()).IsEqualTo(PolyMcpInstructions.Text);
        await Assert.That((await ToolNamesAsync(host)).SequenceEqual(SevenTools)).IsTrue();
    }

    /// <summary>The launch path opencode uses, <c>dotnet run --project … -- mcp</c>, serves the initialize result
    /// as the first stdout line: no MSBuild or host chatter on the stdio stream. A launch-path pin, green on master.</summary>
    [Test]
    public async Task PolycMcp_DotnetRunNoBuild_ServesInitializeCleanly() {
        var root = PolycTestSupport.RepoRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var spec = new ChildSpec("run", [
            "--no-build",
            "--project", Path.Combine(root, "src/polyc/polyc.csproj"),
            "-c", configuration,
            "--", "mcp",
        ]);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var child = await PolycProcess.ManagedChild.StartAsync(spec, deadline.Token);
        using var rpc = new McpClient(child, TimeSpan.FromSeconds(120));

        await child.Process.StandardInput.WriteLineAsync("""
            {"jsonrpc":"2.0","id":99,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"polyc-test","version":"1"}}}
            """.AsMemory(), deadline.Token);
        await child.Process.StandardInput.FlushAsync(deadline.Token);
        var first = await child.Process.StandardOutput.ReadLineAsync(deadline.Token);

        using var message = JsonDocument.Parse(first!);
        await Assert.That(message.RootElement.GetProperty("id").GetInt32()).IsEqualTo(99);
        await Assert.That(message.RootElement.GetProperty("result").GetProperty("instructions").GetString())
            .IsEqualTo(PolyMcpInstructions.Text);

        await child.Process.StandardInput.WriteLineAsync(
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""".AsMemory(), deadline.Token);
        await child.Process.StandardInput.FlushAsync(deadline.Token);
        await Assert.That((await ToolNamesAsync(rpc)).SequenceEqual(SevenTools)).IsTrue();
    }

    [Test]
    public async Task PolyDefine_FindingsUseTheCodesPolycCheckReports() {
        var text = PolycTestSupport.ManyErrors(3);
        var check = new PolyCompiler(ProductCatalog.Catalog, ProductCatalog.ImplicitIds).Compile(text, "<input>");
        var expected = check.Diagnostics.Where(d => d.IsError).Select(d => d.Code).Distinct().Order(StringComparer.Ordinal).ToArray();
        await Assert.That(expected.Length).IsGreaterThan(0);

        using var host = await PolycProcess.McpAsync(McpTarget.Polyc);
        await host.InitializeAsync();
        var workspace = (await host.ToolAsync("poly_workspace", new JsonObject { ["name"] = "Broken" }))["state"]!["workspace"]!.GetValue<string>();
        var define = await host.ToolAsync("poly_define", new JsonObject { ["workspace"] = workspace, ["poly"] = text, ["apply"] = false });
        var findings = define["findings"]!.AsArray();
        var codes = findings.Where(f => f!["severity"]!.GetValue<string>().Equals("Error", StringComparison.OrdinalIgnoreCase))
            .Select(f => f!["code"]!.GetValue<string>()).Distinct().Order(StringComparer.Ordinal).ToArray();
        await Assert.That(codes.SequenceEqual(expected)).IsTrue();

        var placed = check.Diagnostics.First(d => d.IsError && d.Placed);
        var withLine = findings.First(f => f!["line"] is not null)!;
        await Assert.That(withLine["line"]!.GetValue<int>()).IsEqualTo(placed.Line);
        await Assert.That(withLine["column"]!.GetValue<int>()).IsEqualTo(placed.Column);
    }

    [Test]
    public async Task PolyDefine_PreAnalysisFailuresKeepCheckCodes() {
        using var host = await PolycProcess.McpAsync(McpTarget.Polyc);
        await host.InitializeAsync();
        var workspace = (await host.ToolAsync("poly_workspace", new JsonObject { ["name"] = "Broken" }))["state"]!["workspace"]!.GetValue<string>();

        await AssertMatchesCheck(host, workspace, "domain X\nA: entity { X: Text @\n");
        await AssertMatchesCheck(host, workspace, "domain X\nuses nope\n");
    }

    private static async Task AssertMatchesCheck(McpClient host, string workspace, string text) {
        var check = new PolyCompiler(ProductCatalog.Model, ProductCatalog.ImplicitIds).Compile(text, "<workspace>");
        var expected = check.Diagnostics.Single(d => d.IsError);
        var define = await host.ToolAsync("poly_define", new JsonObject { ["workspace"] = workspace, ["poly"] = text, ["apply"] = false });
        var finding = define["findings"]!.AsArray().Single(f =>
            string.Equals(f!["severity"]!.GetValue<string>(), "Error", StringComparison.OrdinalIgnoreCase))!;
        await Assert.That(finding["code"]?.GetValue<string>()).IsEqualTo(expected.Code);
        await Assert.That(finding["line"]!.GetValue<int>()).IsEqualTo(expected.Line);
        await Assert.That(finding["column"]!.GetValue<int>()).IsEqualTo(expected.Column);
        await Assert.That(finding["path"]!.GetValue<string>()).IsEqualTo(expected.Path);
    }

    /// <summary>#533: a wrong JSON type is a typed caller error, and the host keeps serving.</summary>
    [Test]
    public async Task StdioWrongTypedArgument_IsATypedError_AndTheHostStaysAlive() {
        using var host = await PolycProcess.McpAsync(McpTarget.Polyc);
        await host.InitializeAsync();
        var workspace = (await host.ToolAsync("poly_workspace", new JsonObject { ["name"] = "Alive" }))
            ["state"]!["workspace"]!.GetValue<string>();

        var rejected = await host.ToolAsync("poly_simulate", new JsonObject {
            ["workspace"] = workspace,
            ["steps"] = "[]",
            ["reset"] = "yes",
        });
        await Assert.That(rejected["errorCode"]!.GetValue<string>()).IsEqualTo("INVALID_ARGUMENT");
        await Assert.That(rejected["summary"]!.GetValue<string>()).Contains("reset");

        var alive = await host.ToolAsync("poly_workspace", new JsonObject { ["op"] = "list" });
        await Assert.That(alive["ok"]!.GetValue<bool>()).IsTrue();
    }
}