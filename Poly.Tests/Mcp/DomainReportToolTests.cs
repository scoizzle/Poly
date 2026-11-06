using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// The human-facing report through the MCP surface: read-only, and it must render the
/// terms and constraints the model actually has.
/// </summary>
public class DomainReportToolTests {
    private const string Dsl = """
        domain Catalog

        Color: enum { Red, Green }

        Product: entity iri("https://example.com/Product") label("Product") {
          Sku: Text length(1, 20)
          Draft: stage {
            Submit: action { transition to Active }
          }
          Active: stage {}
        }
        """;

    [Test]
    public async Task GetDomainReport_ReturnsMarkdownWithoutMutatingTheSession() {
        var (sessionId, _) = McpTestTools.Store.Create("Catalog");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, Dsl).Success).IsTrue();
        await Assert.That(McpTestTools.Store.TryGet(sessionId, out var afterApply)).IsTrue();
        var revision = afterApply.Revision;

        var response = McpTestTools.Query.GetDomainReport(sessionId);

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Revision).IsEqualTo(revision);
        await Assert.That(response.Data).IsTypeOf<DomainReportData>();
        var data = (DomainReportData)response.Data!;
        await Assert.That(data.EntityCount).IsEqualTo(1);
        await Assert.That(data.Markdown).Contains("# Catalog");
        await Assert.That(data.Markdown).Contains("https://example.com/Product");
        await Assert.That(data.Markdown).Contains("`length(1, 20)`");
        await Assert.That(data.Markdown).Contains("Draft");

        await Assert.That(McpTestTools.Query.GetDomainOverview(sessionId).Revision).IsEqualTo(revision);
    }

    [Test]
    public async Task GetDomainReport_MissingSession_FailsWithAffordances() {
        var response = McpTestTools.Query.GetDomainReport("missing");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Affordances).Contains("create_domain_session");
    }
}