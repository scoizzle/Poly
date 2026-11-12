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

    private const string Prior = """
        domain Board
        Task: entity { Name: Text }
        Board: entity {
          Note: Text optional
          Backlog: many Task
          Active: many Task
          Move: action { for t in Backlog { assign Note to "x" } }
        }
        """;

    private static (PolySurfaceTools Surface, string Workspace, long Revision) WithPriorModel() {
        var surface = Surface();
        var workspace = surface.Workspace(name: "Board").State!.Workspace;
        var defined = surface.Define(workspace, Prior);
        return (surface, workspace, defined.State!.Revision);
    }

    [Test]
    public async Task PolyDefine_Refuses_WithDMEFF020_AndKeepsThePriorModel() {
        var (surface, workspace, revision) = WithPriorModel();
        await Assert.That(revision).IsEqualTo(1);

        var response = surface.Define(workspace, Dsl);

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.Findings.Count(f => f.Code == "DMEFF020")).IsEqualTo(1);
        await Assert.That(response.State!.Revision).IsEqualTo(revision);
        var check = surface.Check(workspace);
        await Assert.That(check.State!.Revision).IsEqualTo(revision);
        await Assert.That(check.State.Entities).IsEqualTo(2);
        await Assert.That(JsonSerializer.Serialize(surface.Export(workspace, "dsl").Data))
            .Contains("assign Note").And.DoesNotContain("link Active");
        await Assert.That(JsonSerializer.Serialize(surface.Inspect(workspace, "Board.Move").Data))
            .DoesNotContain("link Active");
    }

    [Test]
    public async Task PolyDefine_DryRun_RefusesWithDMEFF020_AndChangesNothing() {
        var (surface, workspace, revision) = WithPriorModel();

        var response = surface.Define(workspace, Dsl, apply: false);

        await Assert.That(response.Ok).IsFalse();
        await Assert.That(response.Findings.Count(f => f.Code == "DMEFF020")).IsEqualTo(1);
        await Assert.That(surface.Inspect(workspace).State!.Revision).IsEqualTo(revision);
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