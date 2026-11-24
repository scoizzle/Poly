using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #535, #1101: a recursive invoke cycle is refused at analysis (DMEFF021), guarded or not. The simulator cuts a deep
/// chain at its depth guard, but the printed C# has none and overflows the process, so the hosts would disagree.
/// A cycle of transitions alone is DMEFF013's rule, which still allows a guarded one.
/// </summary>
public class InvokeCycleTests {
    private static EvolutionResult Analyze(string dsl) {
        var changes = new PolyDslParser(dsl).Parse();
        return new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
    }

    private static bool ReportsCycle(EvolutionResult result, string path) =>
        result.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.InvokeCycle
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

    /// <summary>#1101: a guard on an edge does not make recursion safe. The printed C# has no depth guard, so a
    /// guarded cycle that recurses deep overflows the process while the simulator fails cleanly; the cycle is refused
    /// at define, the same answer on every host.</summary>
    [Test]
    public async Task GuardedSelfInvoke_IsRefused_1101() {
        var result = Analyze("""
            domain Probe
            Z: entity {
              N: Number default(0)
              Go: action { if (N >= 0) { invoke Go } }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(ReportsCycle(result, "Go → Go")).IsTrue();
    }

    [Test]
    public async Task ElseBranchMutualInvoke_IsRefused_1101() {
        var result = Analyze("""
            domain Probe
            A: entity {
              Qty: Number range(0, 10) default(0)
              Ping: action { invoke Pong }
              Pong: action { if (Qty > 5) { assign Qty to 0 } else { invoke Ping } }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(ReportsCycle(result, "Ping → Pong → Ping")).IsTrue();
    }

    /// <summary>#1101: recursion across a relationship overflows the printed C# the same way, so it is a cycle too.</summary>
    [Test]
    public async Task CrossEntityCycle_IsRefused_1101() {
        var result = Analyze("""
            domain Probe
            Order: entity {
              N: Number default(0)
              lines: many Line via order
              Go: action { for l in lines { invoke l.Ping } }
            }
            Line: entity {
              order: Order via lines optional
              Ping: action { invoke order.Go }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(ReportsCycle(result, "Order.Go → Line.Ping → Order.Go")).IsTrue();
    }

    /// <summary>#1101: the entry invokes the stage's own Go, and Busy.Go transitions to Open. Open has no entry, so
    /// the chain ends at depth two: the per-copy graph does not merge Open.Go with Busy.Go into one node.</summary>
    [Test]
    public async Task CycleThroughStageEntry_EndsAtDepthTwo_IsAllowed_1101() {
        var result = Analyze("""
            domain Probe
            A: entity {
              N: Number default(0)
              Open: stage { Go: action { transition to Busy } }
              Busy: stage {
                entry { if (N < 3) { invoke Go } }
                Go: action { transition to Open }
              }
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
    }

    /// <summary>#1101: Busy's entry invokes Go, which in Busy is Busy.Go and transitions back into Busy, so the entry
    /// runs again. This copy really recurses and is refused.</summary>
    [Test]
    public async Task StageEntryInvokesTheCopyThatReentersTheStage_IsRefused_1101() {
        var result = Analyze("""
            domain Probe
            A: entity {
              N: Number default(0)
              Busy: stage {
                entry { if (N < 3) { invoke Go } }
                Go: action { transition to Busy }
              }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(ReportsCycle(result, "Go → Busy (entry) → Go")).IsTrue();
    }

    /// <summary>#1101: the entity-level Go transitions to Busy; Busy's entry invokes Go, which in Busy is Busy.Go and
    /// transitions to Open. The two same-named copies never invoke each other's stage path, so there is no cycle. The
    /// stage copy is declared first so adding the entity-level action does not copy its transition into it.</summary>
    [Test]
    public async Task EntityLevelAndStageCopyWithSameName_WithoutMutualRecursion_IsAllowed_1101() {
        var result = Analyze("""
            domain Probe
            A: entity {
              N: Number default(0)
              Busy: stage {
                entry { if (N < 3) { invoke Go } }
                Go: action { transition to Open }
              }
              Go: action { transition to Busy }
              Open: stage { }
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
    }

    /// <summary>#1101: one stage's copy of an action is enough to close a cycle (the edges of every copy count).</summary>
    [Test]
    public async Task CycleInOneStageCopyOnly_IsRefused_1101() {
        var result = Analyze("""
            domain Probe
            A: entity {
              Go: action { }
              Busy: stage { Go: action { invoke Go } }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(ReportsCycle(result, "Go → Go")).IsTrue();
    }

    /// <summary>A transition cycle with no action on it stays DMEFF013's rule, which allows a guarded one.</summary>
    [Test]
    public async Task GuardedTransitionCycleThroughEntries_IsNotAnInvokeCycle() {
        var result = Analyze("""
            domain Probe
            A: entity {
              N: Number default(0)
              Open: stage { entry { if (N > 5) { transition to Busy } } }
              Busy: stage { entry { if (N > 5) { transition to Open } } }
            }
            """);

        await Assert.That(result.Analysis.Diagnostics.Any(d => d.Code == DomainModelDiagnosticCodes.InvokeCycle)).IsFalse();
    }

    // Until #1101 these two were accepted: a guard (or a for body that may run zero times) was taken to end the chain,
    // with the runtime depth guard as the backstop. The printed C# has no such backstop, so the owner ruled
    // (2026-11-23) that a recursive cycle is refused whether or not an edge is guarded.

    [Test]
    public async Task GuardedInvoke_IsRefused() {
        var result = Analyze("""
            domain Probe
            A: entity {
              Qty: Number range(0, 10) default(0)
              Ping: action { invoke Pong }
              Pong: action { if (Qty > 5) { invoke Ping } }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(ReportsCycle(result, "Ping → Pong → Ping")).IsTrue();
    }

    [Test]
    public async Task InvokeInsideForEach_IsRefused() {
        var result = Analyze("""
            domain Probe
            Line: entity { N: Number default(0) }
            A: entity {
              lines: many Line
              Ping: action { for l in lines { invoke Ping } }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(ReportsCycle(result, "Ping → Ping")).IsTrue();
    }

    /// <summary>A guarded invoke that does not recurse is still accepted.</summary>
    [Test]
    public async Task GuardedInvokeWithoutACycle_IsAllowed() {
        var result = Analyze("""
            domain Probe
            A: entity {
              Qty: Number range(0, 10) default(0)
              Ping: action { if (Qty > 5) { invoke Pong } }
              Pong: action { assign Qty to 0 }
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
    }
}