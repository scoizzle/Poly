using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging.Abstractions;

using Poly.Mcp.Surface;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// The v2 surface must be a projection over the same engine, not a second implementation:
/// its dsl and JSON Schema artifacts are byte-identical to the v1 tools on the same domain.
/// </summary>
public class SurfaceParityTests {
    private static PolySurfaceTools Surface() =>
        new(McpTestTools.Store, NullLogger<PolySurfaceTools>.Instance);

    private static string V1Dsl(string workspace, string poly) {
        var tool = new DslTool(McpTestTools.Store, NullLogger<DslTool>.Instance);
        var applied = tool.ApplyDsl(workspace, poly);
        if (!applied.Success)
            throw new InvalidOperationException(applied.Message);
        return JsonSerializer.SerializeToNode(tool.ExportDsl(workspace).Data)!["poly"]!.GetValue<string>();
    }

    private static string V1Schema(string workspace) {
        var tool = new OracleTool(McpTestTools.Store, NullLogger<OracleTool>.Instance);
        var exported = tool.ExportDomainToJsonSchema(workspace);
        if (!exported.Success)
            throw new InvalidOperationException(exported.Message);
        return JsonSerializer.SerializeToNode(exported.Data)!["json"]!.GetValue<string>();
    }

    private static (string Dsl, string Schema) V2Artifacts(string poly) {
        var surface = Surface();
        var opened = surface.Workspace(name: "Parity");
        var workspace = opened.State!.Workspace;
        var defined = surface.Define(workspace, poly);
        if (!defined.Ok)
            throw new InvalidOperationException(defined.Summary);

        var dsl = JsonSerializer.SerializeToNode(surface.Export(workspace, "dsl", inline: true).Data)!
            ["content"]!.GetValue<string>();
        var schema = JsonSerializer.SerializeToNode(surface.Export(workspace, "json-schema", inline: true).Data)!
            ["content"]!.GetValue<string>();
        return (dsl, schema);
    }

    private static async Task AssertParity(string poly) {
        var (sessionId, _) = McpTestTools.Store.Create("ParityV1");
        var expectedDsl = V1Dsl(sessionId, poly);
        var expectedSchema = V1Schema(sessionId);

        var (actualDsl, actualSchema) = V2Artifacts(poly);

        await Assert.That(actualDsl).IsEqualTo(expectedDsl);
        await Assert.That(actualSchema).IsEqualTo(expectedSchema);
    }

    [Test]
    public async Task Parity_SimpleLifecycle() => await AssertParity("""
        domain Simple
        Item: entity {
          Name: Text
          Qty: Number range(0, 10)
          Draft: stage { Submit: action { transition to Active } }
          Active: stage {}
        }
        """);

    [Test]
    public async Task Parity_TermsAndConstraints() => await AssertParity("""
        domain Terms
        Color: enum iri("https://example.com/Color") {
          Red term("red"),
          Green,
        }
        Product: entity iri("https://example.com/Product") label("Product") description("A product") {
          Sku: Text length(1, 12) pattern("^[A-Z]+$")
          Shade: Color default(Red)
          Note: Text optional
        }
        """);

    [Test]
    public async Task Parity_Navigation() => await AssertParity("""
        domain Rel
        Line: entity { Sku: Text }
        Order: entity { Total: Number lines: many Line }
        """);

    [Test]
    public async Task Parity_PolicyAndAction() => await AssertParity("""
        domain Policy
        Account: entity {
          Balance: Number range(0, 100)
          InCredit: policy { Balance > 0 }
          Withdraw: action (amount: Number) require InCredit {
            assign Balance to Balance - amount
          }
        }
        """);
}