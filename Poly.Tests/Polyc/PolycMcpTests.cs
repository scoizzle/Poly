using System.Text.Json.Nodes;

using Poly.Compile;
using Poly.Packs.Product;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Polyc;

/// <summary>Scripted stdio client against the real hosts: <c>polyc mcp</c> and the Poly.Mcp forwarder.
/// Both are started through <see cref="PolycProcess.McpAsync"/>, so each host holds a gate slot and is
/// killed as a tree on dispose.</summary>
public class PolycMcpTests {
    private static readonly string[] SevenTools = [
        "poly_check", "poly_define", "poly_export", "poly_guide", "poly_inspect", "poly_simulate", "poly_workspace",
    ];

    private static async Task<string[]> ToolNamesAsync(McpClient host) =>
        [.. (await host.ToolsListAsync()).Select(t => t["name"]!.GetValue<string>()).Order(StringComparer.Ordinal)];

    [Test]
    public async Task PolycMcp_ListsExactlyTheSevenTools() {
        using var host = await PolycProcess.McpAsync(McpTarget.Polyc);
        await host.InitializeAsync();
        await Assert.That((await ToolNamesAsync(host)).SequenceEqual(SevenTools)).IsTrue();
    }

    [Test]
    public async Task PolyDefine_FindingsUseTheCodesPolycCheckReports() {
        var text = PolycTestSupport.ManyErrors(3);
        var check = new PolyCompiler(ProductCatalog.Catalog, "authoring").Compile(text, "<input>");
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
        var check = new PolyCompiler(ProductCatalog.Model, "authoring").Compile(text, "<workspace>");
        var expected = check.Diagnostics.Single(d => d.IsError);
        var define = await host.ToolAsync("poly_define", new JsonObject { ["workspace"] = workspace, ["poly"] = text, ["apply"] = false });
        var finding = define["findings"]!.AsArray().Single(f =>
            string.Equals(f!["severity"]!.GetValue<string>(), "Error", StringComparison.OrdinalIgnoreCase))!;
        await Assert.That(finding["code"]?.GetValue<string>()).IsEqualTo(expected.Code);
        await Assert.That(finding["line"]!.GetValue<int>()).IsEqualTo(expected.Line);
        await Assert.That(finding["column"]!.GetValue<int>()).IsEqualTo(expected.Column);
        await Assert.That(finding["path"]!.GetValue<string>()).IsEqualTo(expected.Path);
    }

    /// <summary>Two hosts, sequential rather than concurrent: each holds one of the two gate slots, and the
    /// forwarder's result is compared against what the direct host reported before it was killed.</summary>

    [Test]
    public async Task PolyMcpForwarder_StartsTheSameHost() {
        string instructions;
        string[] polycTools;
        using (var polyc = await PolycProcess.McpAsync(McpTarget.Polyc)) {
            var initialized = await polyc.InitializeAsync();
            instructions = initialized["instructions"]!.GetValue<string>();
            polycTools = await ToolNamesAsync(polyc);
        }

        using var forwarder = await PolycProcess.McpAsync(McpTarget.PolyMcp);
        var viaForwarder = await forwarder.InitializeAsync();
        await Assert.That(viaForwarder["instructions"]!.GetValue<string>()).IsEqualTo(instructions);
        var forwarderTools = await ToolNamesAsync(forwarder);
        await Assert.That(forwarderTools.SequenceEqual(SevenTools)).IsTrue();
        await Assert.That(forwarderTools.SequenceEqual(polycTools)).IsTrue();
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