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
        var surface = PolySurfaceHarness.Over(McpTestTools.Store);
        var sessionId = surface.Open("Inventory");
        await Assert.That(surface.Define(sessionId, Dsl).Success).IsTrue();
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

        var inspected = surface.Inspect(sessionId).Revision;
        await Assert.That(inspected).IsNotNull();
        await Assert.That(inspected).IsEqualTo(revision);
    }

    [Test]
    public async Task ExportDomainToJsonLd_MissingSession_FailsWithAffordances() {
        var response = McpTestTools.Oracle.ExportDomainToJsonLd("missing");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Affordances).Contains("create_domain_session");
    }
}