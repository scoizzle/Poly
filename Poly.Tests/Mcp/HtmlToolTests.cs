using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// The MCP door over the HTML docs exporter: read-only, and the document carries the term
/// IRIs a human needs to understand what was modeled.
/// </summary>
public class HtmlToolTests {
    private const string Dsl = """
        domain Inventory

        Item: entity iri("https://example.com/Item") label("Item") {
          Sku: Text length(3, 12)
          Quantity: Number range(0, 1000)
        }
        """;

    [Test]
    public async Task ExportDomainToHtml_ReturnsDocumentWithoutMutatingTheSession() {
        var surface = McpTestTools.Surface;
        var sessionId = surface.Open("Inventory");
        await Assert.That(surface.Define(sessionId, Dsl).Success).IsTrue();
        await Assert.That(McpTestTools.Store.TryGet(sessionId, out var afterApply)).IsTrue();
        var revision = afterApply.Revision;

        var response = McpTestTools.Oracle.ExportDomainToHtml(sessionId);

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Revision).IsEqualTo(revision);
        await Assert.That(response.Data).IsTypeOf<OracleTool.HtmlExportData>();
        var data = (OracleTool.HtmlExportData)response.Data!;
        await Assert.That(data.EntityCount).IsEqualTo(1);
        await Assert.That(data.Html).Contains("<!doctype html>");
        await Assert.That(data.Html).Contains("https://example.com/Item");
        await Assert.That(data.Html).Contains("<code>range(0, 1000)</code>");

        var inspected = surface.Inspect(sessionId).Revision;
        await Assert.That(inspected).IsNotNull();
        await Assert.That(inspected).IsEqualTo(revision);
    }

    [Test]
    public async Task ExportDomainToHtml_DomainWithAnalysisErrors_FailsClosed() {
        var (sessionId, state) = McpTestTools.Store.Create("Broken");
        var broken = new Domain("Broken", [
            new Entity(
                "Item",
                [new Property("Missing", new DomainTypeReference("NoSuchType"), [])],
                [], [], [])
        ]);
        var analysis = DomainModelAnalyzer.Analyze(broken);
        await Assert.That(analysis.HasErrors).IsTrue();
        McpTestTools.Store.Replace(sessionId, broken, analysis, state.Modeling.WithDomain(broken));

        var response = McpTestTools.Oracle.ExportDomainToHtml(sessionId);

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("analysis errors");
        await Assert.That(response.Data).IsNull();
    }

    [Test]
    public async Task ExportDomainToHtml_MissingSession_FailsWithAffordances() {
        var response = McpTestTools.Oracle.ExportDomainToHtml("missing");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Affordances).Contains("create_domain_session");
    }
}