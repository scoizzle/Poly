using System.Text.Json.Nodes;

using Poly.Tests.TestHelpers;

namespace Poly.Tests.Mcp;

/// <summary>
/// The wire contract for caller mistakes. One filter, derived from each tool's published
/// <c>inputSchema</c>, runs before the SDK binder, so an unknown name, a wrong JSON type, or a
/// missing workspace comes back as a typed envelope instead of <c>INTERNAL_ERROR</c>.
/// </summary>
public class McpProtocolTests {
    private const string Valid = """
        domain V2
        Product: entity {
          Total: Number range(0, 100)
          Draft: stage { Submit: action { transition to Active } }
          Active: stage {}
        }
        """;

    [Test]
    public async Task CallerErrors_ReturnTypedErrors_AtTheWire() {
        using var host = await PolycProcess.McpAsync(McpTarget.Polyc, TimeSpan.FromSeconds(20));
        await host.InitializeAsync();
        var workspace = await OpenWorkspaceAsync(host);

        // One defect per row: every other argument in the row is valid. Every row runs before
        // any assertion, so one run reports all of them.
        var rows = new (string Row, string Tool, JsonObject Arguments, string Code, string[] Fragments)[] {
            ("reset:\"yes\" names reset/boolean", "poly_simulate",
                new JsonObject { ["workspace"] = workspace, ["steps"] = "[]", ["reset"] = "yes" },
                "INVALID_ARGUMENT", ["reset", "boolean"]),
            ("a missing required workspace", "poly_define",
                new JsonObject { ["poly"] = Valid },
                "WORKSPACE_REQUIRED", ["poly_workspace"]),
            ("workspace:42 is the wrong type", "poly_define",
                new JsonObject { ["workspace"] = 42, ["poly"] = Valid },
                "INVALID_ARGUMENT", ["workspace", "string"]),
            // #488's repro. M-07 widens `steps` to array-or-string and inverts this assertion.
            ("steps as a JSON array is invalid until M-07 accepts arrays", "poly_simulate",
                new JsonObject { ["workspace"] = workspace, ["steps"] = new JsonArray() },
                "INVALID_ARGUMENT", ["steps", "string"]),
            // Green on master: the unknown-name error is not folded into INVALID_ARGUMENT.
            ("an unknown argument still names the valid ones", "poly_guide",
                new JsonObject { ["bogus"] = 1 },
                "UNKNOWN_ARGUMENT", ["Valid: topic"]),
        };

        var observed = new List<(string Code, string Summary)>();
        foreach (var row in rows) {
            var result = await host.ToolAsync(row.Tool, row.Arguments);
            observed.Add((result["errorCode"]?.GetValue<string>() ?? "<none>",
                result["summary"]?.GetValue<string>() ?? ""));
        }

        var table = string.Join("\n", rows.Select((row, i) => $"  {row.Row} => {observed[i].Code}: {observed[i].Summary}"));
        for (var i = 0; i < rows.Length; i++) {
            await Assert.That(observed[i].Code).IsEqualTo(rows[i].Code)
                .Because($"{rows[i].Row}\nobserved:\n{table}");
            foreach (var fragment in rows[i].Fragments)
                await Assert.That(observed[i].Summary).Contains(fragment)
                    .Because($"{rows[i].Row}\nobserved:\n{table}");
        }
    }

    /// <summary>
    /// A4 characterisation: both blank and explicit-null workspace already reach
    /// <c>TryWorkspace</c> and produce the one <c>WORKSPACE_REQUIRED</c> envelope, so the
    /// pre-binder filter does not need a second producer for either shape.
    /// </summary>
    [Test]
    public async Task BlankOrNullWorkspace_IsWorkspaceRequired() {
        using var host = await PolycProcess.McpAsync(McpTarget.Polyc, TimeSpan.FromSeconds(20));
        await host.InitializeAsync();

        await AssertTypedAsync(host, "workspace:\"\" is blank",
            "poly_define",
            new JsonObject { ["workspace"] = "", ["poly"] = Valid },
            "WORKSPACE_REQUIRED", "poly_workspace");

        var explicitNull = JsonNode.Parse("""{"workspace":null}""")!.AsObject();
        explicitNull["poly"] = Valid;
        await AssertTypedAsync(host, "workspace:null is explicit null",
            "poly_define",
            explicitNull,
            "WORKSPACE_REQUIRED", "poly_workspace");
    }

    private static async Task<string> OpenWorkspaceAsync(McpClient host) {
        var created = await host.ToolAsync("poly_workspace", new JsonObject { ["name"] = "Protocol" });
        return created["state"]!["workspace"]!.GetValue<string>();
    }

    private static async Task AssertTypedAsync(
        McpClient host, string row, string tool, JsonObject arguments, string code, params string[] fragments) {
        var result = await host.ToolAsync(tool, arguments);
        var errorCode = result["errorCode"]?.GetValue<string>();
        var summary = result["summary"]?.GetValue<string>() ?? "";
        await Assert.That(errorCode).IsEqualTo(code).Because($"{row}: {summary}");
        foreach (var fragment in fragments)
            await Assert.That(summary).Contains(fragment).Because(row);
    }
}
