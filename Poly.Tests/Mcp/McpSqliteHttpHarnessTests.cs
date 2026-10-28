using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// apply_dsl of a sellable-shape domain (<c>uses sqlite</c> / <c>uses http</c>)
/// opens, analyzes, simulates, and exports in the MCP harness.
/// </summary>
public sealed class McpSqliteHttpHarnessTests {
    private const string SellableShape = """
        domain Catalog
        uses temporal
        uses storage
        uses sqlite
        uses http

        Item: entity {
          Name: Text
          Qty: Number
          Bump: action { assign Qty to Qty + 1 }
        }
        """;

    [Test]
    public async Task Apply_SellableShape_OpensAnalyzeSimulateAndExport() {
        var (sessionId, _) = McpTestTools.Store.Create("Catalog");

        var applied = McpTestTools.Dsl.ApplyDsl(sessionId, SellableShape);
        await Assert.That(applied.Success).IsTrue();
        await Assert.That(McpTestTools.Store.TryGet(sessionId, out var state)).IsTrue();
        await Assert.That(state.Domain.Extensions).Contains("sqlite");
        await Assert.That(state.Domain.Extensions).Contains("http");
        await Assert.That(state.Modeling.Extensions).Contains("sqlite");
        await Assert.That(state.Modeling.Extensions).Contains("http");

        var analysis = McpTestTools.Query.GetDomainAnalysis(sessionId);
        await Assert.That(analysis.Success).IsTrue();
        await Assert.That(analysis.Data).IsTypeOf<AnalysisData>();
        await Assert.That(((AnalysisData)analysis.Data!).ErrorCount).IsEqualTo(0);

        var created = McpTestTools.Runtime.CreateInstance(sessionId, "Item", """{"Name":"Widget","Qty":1}""");
        await Assert.That(created.Success).IsTrue();
        await Assert.That(McpTestTools.Store.TryGet(sessionId, out state)).IsTrue();
        var instanceId = state.InstanceMap.First(kvp => kvp.Value.Entity.Name == "Item").Key;
        var bumped = McpTestTools.Runtime.InvokeAction(sessionId, instanceId, "Bump");
        await Assert.That(bumped.Success).IsTrue();

        var exported = McpTestTools.Dsl.ExportDsl(sessionId);
        await Assert.That(exported.Success).IsTrue();
        var poly = exported.Data!.GetType().GetProperty("poly")?.GetValue(exported.Data) as string;
        await Assert.That(poly).IsNotNull();
        await Assert.That(poly!).Contains("uses sqlite");
        await Assert.That(poly).Contains("uses http");

        var csharp = McpTestTools.Oracle.ExportDomainToCSharp(sessionId);
        await Assert.That(csharp.Success).IsTrue();
        var fileCount = (int)csharp.Data!.GetType().GetProperty("fileCount")!.GetValue(csharp.Data)!;
        var files = (string[])csharp.Data.GetType().GetProperty("files")!.GetValue(csharp.Data)!;
        await Assert.That(fileCount).IsGreaterThan(0);
        await Assert.That(files.Any(f => string.Equals(f, "Item.cs", StringComparison.Ordinal))).IsTrue();
        await Assert.That(files.Any(f => string.Equals(f, "Program.cs", StringComparison.Ordinal))).IsFalse();
        await Assert.That(files.Any(f => string.Equals(f, "demo.http", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Apply_UnknownExtension_FailClosed() {
        var (sessionId, _) = McpTestTools.Store.Create("Nope");

        var applied = McpTestTools.Dsl.ApplyDsl(sessionId, """
            domain Catalog
            uses nope

            Item: entity { Name: Text }
            """);
        await Assert.That(applied.Success).IsFalse();
        await Assert.That(applied.Message).Contains("Unknown domain extension 'nope'");
    }
}