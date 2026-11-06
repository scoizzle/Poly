using System.Text.Json.Nodes;

using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// The MCP door over the core JSON Schema exporter. It is a read-only projection: it must
/// not bump the revision, and it must report the same constraints the analysis accepted.
/// </summary>
public class JsonSchemaToolTests {
    private const string Dsl = """
        domain Inventory

        Item: entity {
          Sku: Text length(3, 12) pattern("^[A-Z0-9-]+$")
          Quantity: Number range(0, 1000)
          Note: Text optional
        }
        """;

    [Test]
    public async Task ExportDomainToJsonSchema_ReturnsSchemaWithoutMutatingTheSession() {
        var (sessionId, _) = McpTestTools.Store.Create("Inventory");
        var applied = McpTestTools.Dsl.ApplyDsl(sessionId, Dsl);
        await Assert.That(applied.Success).IsTrue();
        await Assert.That(McpTestTools.Store.TryGet(sessionId, out var afterApply)).IsTrue();
        var revision = afterApply.Revision;

        var response = McpTestTools.Oracle.ExportDomainToJsonSchema(sessionId);

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Revision).IsEqualTo(revision);
        await Assert.That(response.Data).IsTypeOf<OracleTool.JsonSchemaExportData>();
        var data = (OracleTool.JsonSchemaExportData)response.Data!;
        await Assert.That(data.EntityCount).IsEqualTo(1);

        var schema = JsonNode.Parse(data.Json)!;
        await Assert.That(schema["$schema"]!.GetValue<string>())
            .IsEqualTo("https://json-schema.org/draft/2020-12/schema");
        var quantity = schema["$defs"]!["Item"]!["properties"]!["Quantity"]!;
        await Assert.That(quantity["minimum"]!.GetValue<int>()).IsEqualTo(0);
        await Assert.That(quantity["maximum"]!.GetValue<int>()).IsEqualTo(1000);

        // The tool is a projection, not a compile step: the session revision is unchanged.
        var overview = McpTestTools.Query.GetDomainOverview(sessionId);
        await Assert.That(overview.Revision).IsEqualTo(revision);
    }

    [Test]
    public async Task ExportDomainToJsonSchema_DomainWithAnalysisErrors_FailsClosed() {
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

        var response = McpTestTools.Oracle.ExportDomainToJsonSchema(sessionId);

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("analysis errors");
        await Assert.That(response.Data).IsNull();
    }

    [Test]
    public async Task ExportDomainToJsonSchema_MissingSession_FailsWithAffordances() {
        var response = McpTestTools.Oracle.ExportDomainToJsonSchema("missing");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Affordances).Contains("create_domain_session");
    }
}