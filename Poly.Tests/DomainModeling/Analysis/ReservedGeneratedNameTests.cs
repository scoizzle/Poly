using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// N4: user-declared types must not reuse names lowering emits at module scope
/// (<c>{Entity}Stage</c> for an entity that has stages, and <c>DomainResult</c>).
/// </summary>
public sealed class ReservedGeneratedNameTests {
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
    public async Task OrderStageEnum_WithOrderStages_IsAnalyzeError() {
        var analysis = Analyze("""
            domain Shop
            OrderStage: enum { Low, High }
            Order: entity {
              Lvl: OrderStage default(Low)
              Draft: stage {}
              Placed: stage {}
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ReservedGeneratedName);
        await Assert.That(errors.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(errors[0].Message).Contains("OrderStage")
            .And.Contains("stage enum")
            .And.Contains("Order")
            .And.Contains("rename");
    }

    [Test]
    public async Task OrderStageEntity_WithOrderStages_IsAnalyzeError() {
        var analysis = Analyze("""
            domain Shop
            OrderStage: entity {
              Name: Text required
            }
            Order: entity {
              Draft: stage {}
              Placed: stage {}
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ReservedGeneratedName);
        await Assert.That(errors.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(errors[0].Message).Contains("OrderStage")
            .And.Contains("stage enum")
            .And.Contains("Order");
    }

    [Test]
    public async Task DomainResultEntity_IsAnalyzeError() {
        var analysis = Analyze("""
            domain Clash
            DomainResult: entity {
              Name: Text required
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ReservedGeneratedName);
        await Assert.That(errors.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(errors[0].Message).Contains("DomainResult")
            .And.Contains("scaffolding")
            .And.Contains("rename");
    }

    [Test]
    public async Task DomainResultEnum_IsAnalyzeError() {
        var analysis = Analyze("""
            domain Clash
            DomainResult: enum { Ok, Err }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.ReservedGeneratedName);
        await Assert.That(errors.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(errors[0].Message).Contains("DomainResult");
    }

    [Test]
    public async Task OrderStatusEnum_WithOrderStages_IsAllowed() {
        var analysis = Analyze("""
            domain Shop
            OrderStatus: enum { Low, High }
            Order: entity {
              Lvl: OrderStatus default(Low)
              Draft: stage {}
              Placed: stage {}
            }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.ReservedGeneratedName)).IsEmpty();
        await Assert.That(analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    [Test]
    public async Task FooStageEnum_WhenNoEntityFoo_IsAllowed() {
        var analysis = Analyze("""
            domain Shop
            FooStage: enum { Low, High }
            Order: entity {
              Lvl: FooStage default(Low)
              Draft: stage {}
              Placed: stage {}
            }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.ReservedGeneratedName)).IsEmpty();
        await Assert.That(analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    [Test]
    public async Task FooStageEnum_WhenFooHasNoStages_IsAllowed() {
        var analysis = Analyze("""
            domain Shop
            FooStage: enum { Low, High }
            Foo: entity {
              Lvl: FooStage default(Low)
            }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.ReservedGeneratedName)).IsEmpty();
        await Assert.That(analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    [Test]
    public async Task TwoDomainTypesWithSameName_IsStructuralDuplicate() {
        // Catalog-level unique-name (DMSTR001) — already enforced by ReportDuplicateNames.
        var analysis = Analyze("""
            domain Clash
            Status: enum { A, B }
            Status: entity {
              Name: Text required
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.StructuralDuplicate);
        await Assert.That(errors.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(errors[0].Message).Contains("Status");
    }
}