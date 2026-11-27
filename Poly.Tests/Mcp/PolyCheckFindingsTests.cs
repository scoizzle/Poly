using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging.Abstractions;

using Poly.Mcp.Surface;

namespace Poly.Tests.Mcp;

/// <summary>
/// #506: <c>poly_check</c> reports <c>hintCount</c> but never lists the hints, and a whole-model
/// finding carries no line/column although the same finding from <c>poly_define</c> is placed.
/// The document's own outline places both, so the two tools name the same source line.
/// </summary>
public class PolyCheckFindingsTests {
    /// <summary>A collection nothing writes: DMREL002, a warning with an element path.</summary>
    private const string Unwritten = """
        domain Shop
        Order: entity {
          Total: Number
          lines: many Line
          HasLines: policy { count lines > 0 }
          Get: action require HasLines { }
        }
        Line: entity { Value: Number }
        """;

    /// <summary>An entity with no stages or policies, which the analyzer answers with hints.</summary>
    private const string Suggestions = """
        domain P
        A: entity { Q: Number range(0, 10) }
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
    public async Task Check_PlacesAWholeModelFinding_WhereDefinePlacesIt() {
        var surface = Surface();
        var workspace = surface.Workspace(name: "Positions").State!.Workspace;

        // A dry run is the define half that lists the findings; apply lists only the counts.
        var dry = surface.Define(workspace, Unwritten, apply: false);
        var fromDefine = dry.Findings.FirstOrDefault(f => f.Code == "DMREL002")
            ?? throw new InvalidOperationException("dry-run codes: " + string.Join(",", dry.Findings.Select(f => f.Code)));
        await Assert.That(fromDefine.Line).IsNotNull().Because("poly_define places the finding");

        var defined = surface.Define(workspace, Unwritten);
        await Assert.That(defined.Ok).IsTrue();

        var response = surface.Check(workspace);
        var fromCheck = response.Findings.Single(f => f.Code == "DMREL002");

        await Assert.That(fromCheck.Line).IsEqualTo(fromDefine.Line);
        await Assert.That(fromCheck.Column).IsEqualTo(fromDefine.Column);
    }

    [Test]
    public async Task Check_ListsTheHints_ItsSummaryCounts() {
        var surface = Surface();
        var workspace = Define(surface, "Hints", Suggestions);

        var data = Data(surface.Check(workspace));
        var hintCount = data["hintCount"]!.GetValue<int>();
        await Assert.That(hintCount).IsGreaterThan(0);

        var hints = data["hints"]!.AsArray();
        await Assert.That(hints.Count).IsEqualTo(hintCount);
        foreach (var hint in hints)
            await Assert.That(hint!["severity"]!.GetValue<string>()).IsEqualTo("Hint");
        await Assert.That(hints[0]!["message"]!.GetValue<string>()).IsNotEmpty();
    }
}