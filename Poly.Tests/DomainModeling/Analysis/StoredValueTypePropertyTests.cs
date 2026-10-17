using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// A domain <c>value</c> type is a contract payload / action parameter with no storage
/// representation. A stored entity property typed as one used to pass analysis and fail
/// later: the DbContext emitted an unmappable column (<c>EnsureCreated</c> threw) and the
/// VM's <c>create in</c> path threw <c>Member 'IsSuccess' is not resolved</c>. Analysis
/// now fails closed (DMSTR006); the contract-payload / action-parameter shapes stay legal.
/// </summary>
public sealed class StoredValueTypePropertyTests {
    private static AnalysisResult Analyze(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(
            new PolyDslParser(dsl).Parse());
        return result.Succeeded ? DomainModelAnalyzer.Analyze(result.Root!) : result.Analysis;
    }

    private static IReadOnlyList<Diagnostic> Errors(AnalysisResult analysis, string code) =>
        analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Code == code)
            .ToList();

    [Test]
    public async Task ValueTypeProperty_OnEntity_IsAnalyzeError() {
        var analysis = Analyze("""
            domain Shop
            Money: value {
              Amount: Number
              Currency: Text
            }
            Order: entity {
              Bill: Money
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.StoredValueTypeProperty);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message)
            .Contains("Order")
            .And.Contains("Bill")
            .And.Contains("Money")
            .And.Contains("no storage representation")
            .And.Contains("nested entity");
    }

    [Test]
    public async Task ValueTypeProperty_OnEntity_FailsEvolutionClosed() {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(
            new PolyDslParser("""
                domain Shop
                Money: value { Amount: Number }
                Order: entity { Bill: Money }
                """).Parse());

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Code == DomainModelDiagnosticCodes.StoredValueTypeProperty)).IsTrue();
    }

    [Test]
    public async Task ValueType_AsLocalActionParameter_StaysLegal() {
        var analysis = Analyze("""
            domain Shop
            Money: value {
              Amount: Number
              Currency: Text
            }
            Order: entity {
              Total: Number default(0)
              Charge: action (bill: Money) {
                assign Total to 0
              }
            }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.StoredValueTypeProperty)).IsEmpty();
        await Assert.That(analysis.HasErrors).IsFalse();
    }

    [Test]
    public async Task ContractValueType_AsPayloadAndBoundParameter_StaysLegal() {
        // crm.poly declares `ChargeRequest: value` inside a contract and binds it to an
        // action parameter. That stays legal — only stored entity properties are rejected.
        var poly = File.ReadAllText(Path.Combine(FindRepoRoot(), "docs/probes/dogfood/crm.poly"));
        var analysis = Analyze(poly);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.StoredValueTypeProperty)).IsEmpty();
        await Assert.That(analysis.HasErrors).IsFalse();
    }

    private static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir, "Poly.sln"))
                || File.Exists(Path.Combine(dir, "docs/CORE.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root from " + AppContext.BaseDirectory);
    }
}