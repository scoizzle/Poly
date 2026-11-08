using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

using Poly.Compile;
using Poly.Packs.Product;

namespace Poly.Tests.Polyc;

/// <summary>Scripted stdio client against the real hosts: <c>polyc mcp</c> and the Poly.Mcp forwarder.</summary>
public class PolycMcpTests {
    private static readonly string[] SevenTools = [
        "poly_check", "poly_define", "poly_export", "poly_guide", "poly_inspect", "poly_simulate", "poly_workspace",
    ];

    private sealed class McpProcess : IDisposable {
        private readonly Process _process;
        private int _id;

        public McpProcess(string dll, params string[] args) {
            var info = new ProcessStartInfo("dotnet") {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            info.ArgumentList.Add(dll);
            foreach (var arg in args)
                info.ArgumentList.Add(arg);
            _process = Process.Start(info)!;
            _process.ErrorDataReceived += (_, _) => { };
            _process.BeginErrorReadLine();
        }

        public JsonNode Initialize() {
            var response = Call("initialize", new JsonObject {
                ["protocolVersion"] = "2025-06-18",
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject { ["name"] = "polyc-test", ["version"] = "1" },
            });
            Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" });
            return response["result"]!;
        }

        public JsonNode Call(string method, JsonObject parameters) {
            var id = ++_id;
            Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
            while (true) {
                var line = ReadLine();
                var message = JsonNode.Parse(line)!;
                if (message["id"]?.GetValue<int>() == id)
                    return message;
            }
        }

        public JsonNode Tool(string name, JsonObject arguments) {
            var response = Call("tools/call", new JsonObject { ["name"] = name, ["arguments"] = arguments });
            var result = response["result"]!;
            return result["structuredContent"] ?? JsonNode.Parse(result["content"]![0]!["text"]!.GetValue<string>())!;
        }

        private void Send(JsonNode message) {
            _process.StandardInput.WriteLine(message.ToJsonString());
            _process.StandardInput.Flush();
        }

        private string ReadLine() {
            var read = _process.StandardOutput.ReadLineAsync();
            if (!read.Wait(TimeSpan.FromSeconds(60)))
                throw new TimeoutException("No MCP message within 60s.");
            return read.Result ?? throw new EndOfStreamException("The MCP host closed stdout.");
        }

        public void Dispose() {
            try {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) {
            }
            _process.Dispose();
        }
    }

    private static string Dll(string name) {
        var beside = Path.Combine(AppContext.BaseDirectory, name + ".dll");
        if (File.Exists(beside) && File.Exists(Path.Combine(AppContext.BaseDirectory, name + ".runtimeconfig.json")))
            return beside;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var project = name == "polyc" ? "src/polyc" : name;
        return Path.Combine(PolycTestSupport.RepoRoot(), project, "bin", configuration, "net10.0", name + ".dll");
    }

    private static string[] ToolNames(McpProcess host) =>
        [.. host.Call("tools/list", new JsonObject())["result"]!["tools"]!.AsArray()
            .Select(t => t!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal)];

    [Test]
    public async Task PolycMcp_ListsExactlyTheSevenTools() {
        using var host = new McpProcess(Dll("polyc"), "mcp");
        host.Initialize();
        await Assert.That(ToolNames(host).SequenceEqual(SevenTools)).IsTrue();
    }

    [Test]
    public async Task PolyDefine_FindingsUseTheCodesPolycCheckReports() {
        var text = PolycTestSupport.ManyErrors(3);
        var check = new PolyCompiler(ProductCatalog.Catalog, "authoring").Compile(text, "<input>");
        var expected = check.Diagnostics.Where(d => d.IsError).Select(d => d.Code).Distinct().Order(StringComparer.Ordinal).ToArray();
        await Assert.That(expected.Length).IsGreaterThan(0);

        using var host = new McpProcess(Dll("polyc"), "mcp");
        host.Initialize();
        var workspace = host.Tool("poly_workspace", new JsonObject { ["name"] = "Broken" })["state"]!["workspace"]!.GetValue<string>();
        var define = host.Tool("poly_define", new JsonObject { ["workspace"] = workspace, ["poly"] = text, ["apply"] = false });
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
    public async Task PolyMcpForwarder_StartsTheSameHost() {
        using var polyc = new McpProcess(Dll("polyc"), "mcp");
        using var forwarder = new McpProcess(Dll("Poly.Mcp"));
        var viaPolyc = polyc.Initialize();
        var viaForwarder = forwarder.Initialize();
        await Assert.That(viaForwarder["instructions"]!.GetValue<string>()).IsEqualTo(viaPolyc["instructions"]!.GetValue<string>());
        await Assert.That(ToolNames(forwarder).SequenceEqual(SevenTools)).IsTrue();
        await Assert.That(ToolNames(forwarder).SequenceEqual(ToolNames(polyc))).IsTrue();
    }
}