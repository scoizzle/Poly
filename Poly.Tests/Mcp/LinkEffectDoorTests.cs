using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;

using Poly.Compile;
using Poly.DomainModeling.Compile;
using Poly.Mcp.Surface;
using Poly.Packs.Product;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Mcp;

/// <summary>
/// F-16 (#690): a domain that uses <c>link</c> or <c>unlink</c> is refused with DMEFF020 at every
/// door, because nothing lowers it until L-13. A refused document never becomes a model, so the later
/// doors (check, simulate, export) have nothing to run.
/// </summary>
public class LinkEffectDoorTests {
    private const string Dsl = """
        domain Board
        Task: entity { Name: Text }
        Board: entity {
          Note: Text optional
          Backlog: many Task
          Active: many Task
          Move: action { for t in Backlog { link Active to t } }
        }
        """;

    private static PolySurfaceTools Surface() =>
        new(McpTestTools.Store, NullLogger<PolySurfaceTools>.Instance);

    [Test]
    public async Task PolyDefine_Refuses_WithDMEFF020() {
        var surface = Surface();
        var workspace = surface.Workspace(name: "Board").State!.Workspace;

        var response = surface.Define(workspace, Dsl);

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.Findings.Count(f => f.Code == "DMEFF020")).IsEqualTo(1);
        await Assert.That(response.State!.Revision).IsEqualTo(0);
    }

    [Test]
    public async Task RefusedDocument_LeavesNothingForCheckSimulateOrExport() {
        var surface = Surface();
        var workspace = surface.Workspace(name: "Board").State!.Workspace;
        await Assert.That(surface.Define(workspace, Dsl).Ok).IsFalse();

        var check = surface.Check(workspace);
        var simulate = surface.Simulate(workspace, """[{"op":"create","entity":"Board","properties":{}}]""");
        var csharp = surface.Export(workspace, "csharp");

        await Assert.That(check.State!.Entities).IsEqualTo(0);
        await Assert.That(JsonSerializer.SerializeToNode(simulate.Data)!["failedAt"]).IsNotNull();
        await Assert.That(JsonSerializer.Serialize(csharp.Data)).DoesNotContain("Backlog");
    }

    [Test]
    public async Task PolycBuild_Refuses_AtTheActionPlacement() {
        var compiler = new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.Seed("authoring")));

        var result = compiler.Compile(Dsl, "board.poly");

        var refusal = result.Diagnostics.Single(d => d.Code == "DMEFF020");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Files.Count).IsEqualTo(0);
        await Assert.That(refusal.IsError).IsTrue();
        await Assert.That(refusal.Placed).IsTrue();
        await Assert.That(refusal.Line).IsEqualTo(7);
    }
}