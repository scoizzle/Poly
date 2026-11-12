using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging.Abstractions;

using Poly.Mcp.Surface;

namespace Poly.Tests.Mcp;

/// <summary>
/// #466 S3: poly_check surfaces the analyzer's per-action range/precondition facts as
/// <c>data.witness</c> when select names an action. Read-only; the verdict rules are unchanged.
/// </summary>
public class PolyCheckWitnessTests {
    private const string WithInvariant = """
        domain Witness
        Item: entity {
          Qty: Number range(0, 100) default(0)
          Big: policy { Qty >= 10 }
          Run: action
            require Big
          {
            assign Qty to 50
          }
        }
        """;

    private const string NoInvariant = """
        domain Plain
        Item: entity {
          Qty: Number range(0, 100) default(0)
        }
        """;

    private static PolySurfaceTools Surface() =>
        new(McpTestTools.Store, NullLogger<PolySurfaceTools>.Instance);

    private static string Define(PolySurfaceTools surface, string name, string source) {
        var workspace = surface.Workspace(name: name).State!.Workspace;
        var defined = surface.Define(workspace, source);
        if (!defined.Ok)
            throw new InvalidOperationException(string.Join("; ", defined.Findings.Select(f => f.Message)));
        return workspace;
    }

    private static JsonNode Data(PolyResponse response) => JsonSerializer.SerializeToNode(response.Data)!;

    [Test]
    public async Task Check_SelectAction_ReturnsWitnessContexts_WhenTheAnalysisPublishedAnInvariant() {
        var surface = Surface();
        var workspace = Define(surface, "WitnessA", WithInvariant);

        var data = Data(surface.Check(workspace, select: "Item.Run"));

        await Assert.That(data["verdict"]!.GetValue<string>()).IsEqualTo("proven");
        var witness = data["witness"]!.AsArray();
        await Assert.That(witness.Count).IsEqualTo(1);
        var context = witness[0]!;
        await Assert.That(context["stageName"]).IsNull();
        await Assert.That(context["preconditions"]!.AsArray().Select(n => n!.GetValue<string>())).Contains("Big");
        var qty = context["properties"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "Qty")!;
        await Assert.That(qty["min"]!.GetValue<decimal>()).IsEqualTo(10m);
        await Assert.That(qty["max"]!.GetValue<decimal>()).IsEqualTo(100m);
        var post = context["postconditions"]!.AsArray().Single(p => p!["targetProperty"]!.GetValue<string>() == "Qty")!;
        await Assert.That(post["min"]!.GetValue<decimal>()).IsEqualTo(50m);
    }

    [Test]
    public async Task Check_SelectActionWithArgs_StaysUnknownAndStillCarriesTheWitness() {
        var surface = Surface();
        var workspace = Define(surface, "WitnessB", WithInvariant);

        var response = surface.Check(workspace, select: "Item.Run", args: """{"delta":500}""");

        var data = Data(response);
        await Assert.That(data["verdict"]!.GetValue<string>()).IsEqualTo("unknown");
        await Assert.That(response.Summary).Contains("argument-specific proofs are not implemented");
        await Assert.That(data["witness"]!.AsArray().Count).IsEqualTo(1);
    }

    [Test]
    public async Task Check_SelectActionWithoutInvariant_HasNoWitness() {
        var surface = Surface();
        var workspace = Define(surface, "WitnessC", NoInvariant);

        var whole = Data(surface.Check(workspace, select: "Item"));
        var unmatched = Data(surface.Check(workspace, select: "Item.Nope"));

        await Assert.That(whole["witness"]).IsNull();
        await Assert.That(unmatched["witness"]).IsNull();
    }
}