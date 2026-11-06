using System.Text.Json.Nodes;

using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// The MCP door over the JSON-LD exporter. Like the other exports it is a read-only
/// projection: it must not bump the revision and must carry the declared term IRIs.
/// </summary>
public class JsonLdToolTests {
    private const string Dsl = """
        domain Inventory

        Item: entity iri("https://example.com/Item") label("Item") {
          Sku: Text iri("https://example.com/Item/sku") length(3, 12)
        }
        """;

    [Test]
    public async Task ExportDomainToJsonLd_ReturnsGraphWithoutMutatingTheSession() {
        var (sessionId, _) = McpTestTools.Store.Create("Inventory");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, Dsl).Success).IsTrue();
        await Assert.That(McpTestTools.Store.TryGet(sessionId, out var afterApply)).IsTrue();
        var revision = afterApply.Revision;

        var response = McpTestTools.Oracle.ExportDomainToJsonLd(sessionId);

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Revision).IsEqualTo(revision);
        await Assert.That(response.Data).IsTypeOf<OracleTool.JsonLdExportData>();
        var data = (OracleTool.JsonLdExportData)response.Data!;
        await Assert.That(data.ClassCount).IsEqualTo(1);
        await Assert.That(data.PropertyCount).IsEqualTo(1);

        var jsonLd = JsonNode.Parse(data.JsonLd)!;
        await Assert.That(jsonLd["@context"]!["Item"]!.GetValue<string>())
            .IsEqualTo("https://example.com/Item");
        var item = jsonLd["@graph"]!.AsArray().OfType<JsonObject>()
            .Single(n => n["@id"]!.GetValue<string>() == "https://example.com/Item");
        await Assert.That(item["@type"]!.GetValue<string>()).IsEqualTo("rdfs:Class");

        await Assert.That(McpTestTools.Query.GetDomainOverview(sessionId).Revision).IsEqualTo(revision);
    }

    [Test]
    public async Task ExportDomainToJsonLd_MissingSession_FailsWithAffordances() {
        var response = McpTestTools.Oracle.ExportDomainToJsonLd("missing");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Affordances).Contains("create_domain_session");
    }
}