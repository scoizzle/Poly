using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// <c>validate_dsl</c> is the dry-run half of the authoring loop: an agent must be able to
/// learn whether a document is valid, and why not, without replacing the session domain or
/// clearing runtime instances. A rejected document is the normal case while authoring, so
/// the answer has to carry the diagnostic code and anchor, not just one sentence.
/// </summary>
public class ValidateDslTests {
    private const string ValidDsl = """
        domain ValidateDsl

        Order: entity {
          Total: Number range(0, 100)
          Draft: stage {
            Submit: action { transition to Active }
          }
          Active: stage {}
        }
        """;

    private const string OutOfRangeDsl = """
        domain ValidateDsl

        Order: entity {
          Total: Number range(0, 100)
          Raise: action { assign Total to Total + 250 }
        }
        """;

    [Test]
    public async Task ValidateDsl_ValidDocument_SucceedsWithoutMutatingTheSession() {
        var (sessionId, _) = McpTestTools.Store.Create("ValidateDsl");

        var response = McpTestTools.Dsl.ValidateDsl(sessionId, ValidDsl);

        await Assert.That(response.Success).IsTrue()
            .Because("a document with no analysis errors is valid");
        await Assert.That(response.Data).IsTypeOf<DslValidationData>();
        var data = (DslValidationData)response.Data!;
        await Assert.That(data.Valid).IsTrue();
        await Assert.That(data.DomainName).IsEqualTo("ValidateDsl");
        await Assert.That(data.EntityCount).IsEqualTo(1);
        await Assert.That(data.ErrorCount).IsEqualTo(0);

        // The dry run must not have applied the document.
        var overview = McpTestTools.Query.GetDomainOverview(sessionId);
        var overviewData = (DomainOverviewData)overview.Data!;
        await Assert.That(overviewData.EntityNames).DoesNotContain("Order");
    }

    [Test]
    public async Task ValidateDsl_ConstraintViolation_ReturnsCodedFindingsAndLeavesTheDomainAlone() {
        var (sessionId, _) = McpTestTools.Store.Create("ValidateDsl");

        var response = McpTestTools.Dsl.ValidateDsl(sessionId, OutOfRangeDsl);

        await Assert.That(response.Success).IsFalse()
            .Because("the constraint solver proves the assign can leave range(0, 100)");
        var data = (DslValidationData)response.Data!;
        await Assert.That(data.Valid).IsFalse();
        await Assert.That(data.ErrorCount).IsGreaterThan(0);
        await Assert.That(data.Findings.Any(f => f.Code == "DMEFF008")).IsTrue()
            .Because("a bare message cannot be categorized or anchored; the code must survive");
        await Assert.That(data.Findings.Any(f => f.Message.Contains("range(0, 100)"))).IsTrue();

        // The rejected document did not become the session domain.
        var overview = McpTestTools.Query.GetDomainOverview(sessionId);
        var overviewData = (DomainOverviewData)overview.Data!;
        await Assert.That(overviewData.EntityNames).DoesNotContain("Order");
    }

    [Test]
    public async Task ValidateDsl_ParseError_ReturnsOneDiagnosticAndNoMutation() {
        var (sessionId, _) = McpTestTools.Store.Create("ValidateDsl");

        var response = McpTestTools.Dsl.ValidateDsl(sessionId, "domain Broken { this is not poly }");

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Message).Contains("Parse error");
        var data = (DslValidationData)response.Data!;
        await Assert.That(data.Findings.Count).IsEqualTo(1);
        await Assert.That(data.Findings[0].Severity).IsEqualTo("Error");

        var overview = (DomainOverviewData)McpTestTools.Query.GetDomainOverview(sessionId).Data!;
        await Assert.That(overview.EntityCount).IsEqualTo(0);
    }

    [Test]
    public async Task ApplyDsl_RejectedDocument_ReturnsFindingsAndPointsAtTheDryRun() {
        var (sessionId, _) = McpTestTools.Store.Create("ValidateDsl");

        var response = McpTestTools.Dsl.ApplyDsl(sessionId, OutOfRangeDsl);

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Data).IsTypeOf<DslValidationData>();
        var data = (DslValidationData)response.Data!;
        await Assert.That(data.Findings.Any(f => f.Code == "DMEFF008")).IsTrue();
        await Assert.That(response.Affordances).Contains("validate_dsl")
            .Because("get_domain_analysis describes the unchanged domain, not the rejected document");
    }
}