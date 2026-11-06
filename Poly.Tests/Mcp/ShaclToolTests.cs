using System.Text.Json.Nodes;

using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// The MCP door over the SHACL exporter. Read-only, and the shapes must target the same
/// class IRIs the JSON-LD tool emits.
/// </summary>
public class ShaclToolTests {
    private const string Dsl = """
        domain Inventory

        Item: entity iri("https://example.com/Item") label("Item") {
          Sku: Text iri("https://example.com/Item/sku") length(3, 12)
          Quantity: Number range(0, 1000)
        }
        """;

    [Test]
    public async Task ExportDomainToShacl_ReturnsShapesWithoutMutatingTheSession() {
        var (sessionId, _) = McpTestTools.Store.Create("Inventory");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, Dsl).Success).IsTrue();
        await Assert.That(McpTestTools.Store.TryGet(sessionId, out var afterApply)).IsTrue();
        var revision = afterApply.Revision;

        var response = McpTestTools.Oracle.ExportDomainToShacl(sessionId);

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Revision).IsEqualTo(revision);
        await Assert.That(response.Data).IsTypeOf<OracleTool.ShaclExportData>();
        var data = (OracleTool.ShaclExportData)response.Data!;
        await Assert.That(data.NodeShapeCount).IsEqualTo(1);
        await Assert.That(data.PropertyShapeCount).IsEqualTo(2);

        var shapes = JsonNode.Parse(data.Shapes)!;
        await Assert.That(shapes["@context"]!["sh"]!.GetValue<string>())
            .IsEqualTo("http://www.w3.org/ns/shacl#");
        var node = shapes["@graph"]!.AsArray().OfType<JsonObject>()
            .Single(n => n["@type"]!.GetValue<string>() == "sh:NodeShape");
        await Assert.That(node["sh:targetClass"]!["@id"]!.GetValue<string>())
            .IsEqualTo("https://example.com/Item");

        await Assert.That(McpTestTools.Query.GetDomainOverview(sessionId).Revision).IsEqualTo(revision);
    }

    [Test]
    public async Task ExportDomainToShacl_MissingSession_FailsWithAffordances() {
        var response = McpTestTools.Oracle.ExportDomainToShacl("missing");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Affordances).Contains("create_domain_session");
    }
}