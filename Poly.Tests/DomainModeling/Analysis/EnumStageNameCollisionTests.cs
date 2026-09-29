using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;

namespace Poly.Tests.DomainModeling.Analysis;

public class EnumStageNameCollisionTests {
    [Test]
    public async Task DomainEnumNamedLikeStageEnum_IsRejectedWithClearMessage() {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(
            new PolyDslParser("""
                domain Shop
                OrderStage: enum { Low, High }
                Order: entity {
                  Lvl: OrderStage default(Low)
                  Draft: stage {}
                  Placed: stage {}
                  IsHigh: policy { Lvl is "High" }
                }
                """).Parse());

        var analysis = result.Succeeded ? DomainModelAnalyzer.Analyze(result.Root!) : result.Analysis;
        var errors = analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        await Assert.That(errors.Any(d =>
            d.Message.Contains("OrderStage", StringComparison.Ordinal)
            && d.Message.Contains("stage enum", StringComparison.Ordinal)
            && d.Message.Contains("collides", StringComparison.Ordinal))).IsTrue()
            .Because(string.Join("; ", errors.Select(e => e.Message)));
    }
}
