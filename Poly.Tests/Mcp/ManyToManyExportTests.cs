using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// F33: a ManyToMany relationship (authorable via the incremental add tool) cannot
/// be expressed in the shipped DSL, so export_dsl fails closed instead of silently
/// printing `many` and degrading the cardinality on round-trip.
/// </summary>
public class ManyToManyExportTests {
    private static string AddEntity(string sessionId, string name) {
        var result = McpTestTools.Evolve.Add(sessionId, "entity", $$"""{"name":"{{name}}"}""");
        if (!result.Success)
            throw new InvalidOperationException(result.Message);
        return name;
    }

    [Test]
    public async Task ExportDsl_WithManyToMany_FailsClosedNamingRelationship() {
        var (sessionId, _) = McpTestTools.Store.Create("MnM");
        AddEntity(sessionId, "Order");
        AddEntity(sessionId, "Item");
        var rel = McpTestTools.Evolve.Add(sessionId, "relationship",
            """{"name":"OrderItems","source":"Order","target":"Item","cardinality":"ManyToMany"}""");
        await Assert.That(rel.Success).IsTrue();

        var export = McpTestTools.Dsl.ExportDsl(sessionId);
        await Assert.That(export.Success).IsFalse();
        await Assert.That(export.Message).Contains("OrderItems");
        await Assert.That(export.Message).Contains("ManyToMany");
    }

    [Test]
    public async Task ExportDsl_WithN1Relationships_StillExports() {
        var (sessionId, _) = McpTestTools.Store.Create("N1");
        AddEntity(sessionId, "Order");
        AddEntity(sessionId, "Item");
        var rel = McpTestTools.Evolve.Add(sessionId, "relationship",
            """{"name":"OrderItems","source":"Order","target":"Item","cardinality":"OneToMany"}""");
        await Assert.That(rel.Success).IsTrue();

        var export = McpTestTools.Dsl.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var json = System.Text.Json.JsonSerializer.Serialize(export.Data);
        await Assert.That(json).Contains("OrderItems: many Item");
    }
}