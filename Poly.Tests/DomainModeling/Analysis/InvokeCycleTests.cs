using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #535: an unconditional invoke cycle is refused at analysis. The runtime cuts an invoke chain
/// at a depth guard, so every invocation of an action on such a cycle fails — a model that
/// passes <c>poly_check</c> as <c>Proven</c> must not be one whose actions can never succeed.
/// A cycle behind a real guard may terminate, so it stays allowed (as the automatic-transition
/// cycle rule, DMEFF013, does).
/// </summary>
public class InvokeCycleTests {
    private static EvolutionResult Analyze(string dsl) {
        var changes = new PolyDslParser(dsl).Parse();
        return new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
    }

    private static bool ReportsCycle(EvolutionResult result, string path) =>
        result.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.UnconditionalInvokeCycle
            && d.Message.Contains(path, StringComparison.Ordinal));

    [Test]
    public async Task MutualInvoke_Unconditional_ReportsCycle() {
        var result = Analyze("""
            domain Probe
            A: entity {
              Ping: action { invoke Pong }
              Pong: action { invoke Ping }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(ReportsCycle(result, "Ping → Pong → Ping")).IsTrue();
    }

    [Test]
    public async Task SelfInvoke_Unconditional_ReportsCycle() {
        var result = Analyze("""
            domain Probe
            A: entity {
              Go: action { invoke Go }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(ReportsCycle(result, "Go → Go")).IsTrue();
    }

    [Test]
    public async Task GuardedInvoke_IsAllowed() {
        // The return edge runs only while Qty > 5, so the chain may terminate; the runtime depth
        // guard is the backstop, exactly as for a guarded automatic transition.
        var result = Analyze("""
            domain Probe
            A: entity {
              Qty: Number range(0, 10) default(0)
              Ping: action { invoke Pong }
              Pong: action { if (Qty > 5) { invoke Ping } }
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task InvokeInsideForEach_IsAllowed() {
        // A for body may run zero times, so nothing inside it is unconditional.
        var result = Analyze("""
            domain Probe
            Line: entity { N: Number default(0) }
            A: entity {
              lines: many Line
              Ping: action { for l in lines { invoke Ping } }
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
    }
}