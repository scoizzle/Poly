using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

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

    /// <summary>#1101 review: lowering inlines S2's entry right after the stage assignment, and that entry
    /// transitions back to S1, so the invoke after the transition runs S1.Go again. The stage set after a
    /// transition is not {T} when T's entry can move the instance.</summary>
    [Test]
    public async Task TransitionIntoEntryThatTransitionsBack_IsRefused_1101() {
        var result = Analyze("""
            domain Probe
            A: entity {
              N: Number default(0)
              S1: stage { Go: action {
                  transition to S2
                  invoke Go
              } }
              S2: stage { entry { transition to S1 } }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(ReportsCycle(result, "Go → Go")).IsTrue();
    }

    /// <summary>#1101 review: the same recursion behind a guard; the guard does not break the transition back.</summary>
    [Test]
    public async Task GuardedTransitionIntoEntryThatTransitionsBack_IsRefused_1101() {
        var result = Analyze("""
            domain Probe
            A: entity {
              N: Number default(0)
              S1: stage { Go: action {
                  if (N < 3) {
                    transition to S2
                    invoke Go
                  }
              } }
              S2: stage { entry { transition to S1 } }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(ReportsCycle(result, "Go → Go")).IsTrue();
    }

    /// <summary>#1101 review: S2's entry invokes the entity-level Hop, which transitions back to S1, so the
    /// invoke after the transition again runs S1.Go.</summary>
    [Test]
    public async Task TransitionIntoEntryThatInvokesTransitioningAction_IsRefused_1101() {
        var result = Analyze("""
            domain Probe
            A: entity {
              N: Number default(0)
              S1: stage { Go: action {
                  transition to S2
                  invoke Go
              } }
              S2: stage { entry { invoke Hop } }
              Hop: action { transition to S1 }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(ReportsCycle(result, "Go → Go")).IsTrue();
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

    /// <summary>#1116: an invoke across a relationship can land on this same instance. Here S2's entry
    /// runs B.Nudge, which fans out to every kid — this instance included — and Hop moves it back to S1,
    /// so the <c>invoke Go</c> after the transition runs S1.Go again. The walk must widen the stage set
    /// after a relationship invoke whose reach changes this entity's stage, not assume it cannot.</summary>
    [Test]
    public async Task RelationshipInvokeWhoseEntryMovesThisInstance_IsRefused_1116() {
        var result = Analyze("""
            domain Probe
            B: entity {
              kids: many A via parent
              Nudge: action { for k in kids { invoke k.Hop } }
            }
            A: entity {
              parent: B via kids optional
              S1: stage { Go: action {
                  transition to S2
                  invoke Go
              } }
              S2: stage { entry { invoke parent.Nudge } }
              Hop: action { transition to S1 }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(ReportsCycle(result, "Go → Go")).IsTrue();
    }

    /// <summary>#1116: the same miss without an entry. Kick runs in S2, invokes parent.Nudge, and Hop moves this
    /// instance to S1; the following Go then dispatches S1.Go, which hops back to S2 and invokes Kick again. The
    /// walk saw only {S2} and found no copy of Go there, so the cycle went unreported.</summary>
    [Test]
    public async Task RelationshipInvokeInTheBodyThatMovesThisInstance_IsRefused_1116() {
        var result = Analyze("""
            domain Probe
            B: entity {
              kids: many A via parent
              Nudge: action { for k in kids { invoke k.Hop } }
            }
            A: entity {
              parent: B via kids optional
              S1: stage { Go: action {
                  transition to S2
                  invoke Kick
              } }
              S2: stage { Kick: action {
                  invoke parent.Nudge
                  invoke Go
              } }
              Hop: action { transition to S1 }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(ReportsCycle(result, "Go → Kick → Go")).IsTrue();
    }

    /// <summary>#1116: a relationship invoke whose type-level reach never touches A's stages cannot move A,
    /// so S1.Go's stage set stays {S2} and <c>invoke Go</c> dispatches S2.Go (which never recurses). A rule
    /// that widened on every relationship invoke would see S1.Go again here and refuse a sound model.</summary>
    [Test]
    public async Task RelationshipInvokeWhoseReachNeverMovesThisEntity_IsAllowed_1116() {
        var result = Analyze("""
            domain Probe
            B: entity {
              Open: stage { }
              Busy: stage { Nudge: action { transition to Open } }
            }
            A: entity {
              N: Number default(0)
              b: B optional
              S1: stage { Go: action {
                  transition to S2
                  invoke b.Nudge
                  invoke Go
              } }
              S2: stage { Go: action { assign N to 1 } }
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.InvokeCycle)).IsFalse();
    }

    /// <summary>#1116 review: two sibling conditionals in one body. The reach into B.Nudge keys each branch by its
    /// parent body plus "then"; both branches share that key, so the second one — the only one that reaches Hop —
    /// was skipped and the cycle went unreported. Only action, entry and exit keys may dedupe.</summary>
    [Test]
    public async Task SiblingConditionals_SecondReachesHop_IsRefused() {
        var result = Analyze("""
            domain Probe
            B: entity {
              N: Number default(0)
              kids: many A via parent
              Nudge: action {
                if (N == 0) { assign N to 0 }
                if (N == 0) { for k in kids { invoke k.Hop } }
              }
            }
            A: entity {
              parent: B via kids optional
              S1: stage { Go: action {
                  transition to S2
                  invoke Go
              } }
              S2: stage { entry { invoke parent.Nudge } }
              Hop: action { transition to S1 }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.InvokeCycle)).IsTrue();
    }

    /// <summary>#1116 review: two sibling <c>for</c> loops with the same binder name. The reach keyed a loop body by
    /// its binder name, so the second loop shared the first's key, was skipped, and its invoke of Hop was missed.</summary>
    [Test]
    public async Task SiblingLoopsWithSameBinder_SecondReachesHop_IsRefused() {
        var result = Analyze("""
            domain Probe
            B: entity {
              N: Number default(0)
              cs: many A via cparent
              kids: many A via parent
              Nudge: action {
                for k in cs {
                  assign N to 0
                  invoke k.Ping
                }
                for k in kids {
                  assign N to 0
                  invoke k.Hop
                }
              }
            }
            A: entity {
              cparent: B via cs optional
              parent: B via kids optional
              S1: stage { Go: action {
                  transition to S2
                  invoke Go
              } }
              S2: stage { entry { invoke parent.Nudge } }
              Hop: action { transition to S1 }
              Ping: action { }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.InvokeCycle)).IsTrue();
    }

    /// <summary>#1116 review: a stage entry that loops over a relationship and invokes on the binder. The
    /// entry-may-change-stage check walked the loop body without binding the binder, so the invoke resolved to
    /// nothing and the entry looked stage-preserving; the following <c>invoke Go</c> then found no S2 copy.</summary>
    [Test]
    public async Task EntryLoopBinderInvoke_IsRefused() {
        var result = Analyze("""
            domain Probe
            B: entity {
              owner: A via bs optional
              Poke: action { invoke owner.Hop }
            }
            A: entity {
              N: Number default(0)
              bs: many B via owner
              S1: stage { Go: action {
                  transition to S2
                  invoke Go
              } }
              S2: stage { entry {
                  for b in bs { assign N to 0
                    invoke b.Poke }
              } }
              Hop: action { transition to S1 }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.InvokeCycle)).IsTrue();
    }

    /// <summary>#1116: lowering emits the notify dispatch right after the stage assignment, so a <c>when</c>
    /// handler watching S2 runs before the next effect. Here B's handler fans out to every kid — this instance
    /// included — and Hop moves it back to S1, so the <c>invoke Go</c> after the transition runs S1.Go again. The
    /// walk must widen the stage set after a transition into a stage whose subscribers can move this entity.</summary>
    [Test]
    public async Task WhenSubscriberThatMovesThisInstance_ThenInvokeReentry_IsRefused() {
        var result = Analyze("""
            domain Probe
            B: entity {
              kids: many A via parent
              when kids S2 { for k in kids { invoke k.Hop } }
            }
            A: entity {
              parent: B via kids optional
              S1: stage { Go: action {
                  transition to S2
                  invoke Go
              } }
              S2: stage { }
              Hop: action { transition to S1 }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(ReportsCycle(result, "Go → Go")).IsTrue();
    }

    /// <summary>#1116: a <c>when</c> handler that cannot move the publisher's stage does not widen the stage set,
    /// so S1.Go's stage set stays {S2} and <c>invoke Go</c> dispatches S2.Go, which never recurses. Widening on
    /// every subscriber would refuse a sound model.</summary>
    [Test]
    public async Task WhenSubscriberThatNeverMovesThisInstance_IsAccepted() {
        var result = Analyze("""
            domain Probe
            B: entity {
              N: Number default(0)
              kids: many A via parent
              when kids S2 { assign N to 1 }
            }
            A: entity {
              N: Number default(0)
              parent: B via kids optional
              S1: stage { Go: action {
                  transition to S2
                  invoke Go
              } }
              S2: stage { Go: action { assign N to 1 } }
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.InvokeCycle)).IsFalse();
    }

    /// <summary>#1116: the handler moves this instance, but the action never re-invokes itself after the
    /// transition, so there is no invoke re-entry to refuse. A subscription handler counting as a stage-set
    /// widener does not make it an invoke-graph edge: a cycle driven only by subscriptions is not DMEFF021.</summary>
    [Test]
    public async Task WhenSubscriptionDrivenCycleWithoutInvokeReentry_IsNotDmeff021() {
        var result = Analyze("""
            domain Probe
            B: entity {
              kids: many A via parent
              when kids S2 { for k in kids { invoke k.Hop } }
            }
            A: entity {
              parent: B via kids optional
              S1: stage { Go: action { transition to S2 } }
              S2: stage { }
              Hop: action { transition to S1 }
            }
            """);

        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.InvokeCycle)).IsFalse();
    }

    /// <summary>#1116 Done-when, the runtime half: at the parent commit the scenario is accepted at define and
    /// recurses until the simulator's invoke depth guard stops it. The analyzer now refuses it, so this test
    /// installs the recursive step the analyzer would refuse into an already-lowered module (the #1117 seam):
    /// a clean variant invokes an empty <c>Close</c> where the scenario invokes <c>Go</c>, and that one body is
    /// replaced with <c>invoke Go</c>. The relationship fan-out then moves this instance back to S1 and the
    /// invoke re-enters S1.Go.</summary>
    [Test]
    public async Task RelationshipInvokeScenario_RuntimeDepthGuard_ExceedsDepth_1116() {
        var changes = new PolyDslParser("""
            domain Probe
            B: entity {
              kids: many A via parent
              Nudge: action { for k in kids { invoke k.Hop } }
            }
            A: entity {
              parent: B via kids optional
              S1: stage { Go: action {
                  transition to S2
                  invoke Close
              } }
              S2: stage { entry { invoke parent.Nudge } }
              Hop: action { transition to S1 }
              Close: action { }
            }
            """).Parse();
        var domain = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes).Root;
        var exec = ModuleExec.For(domain);

        var a = domain.Types.OfType<Entity>().Single(e => e.Name == "A");
        var entities = domain.Types.OfType<Entity>().ToArray();
        var recursive = new EffectLoweringPass(a, LoweringFixtures.Context(new ThisReference(), entities))
            .TryLowerVmNode(new InvokeActionEffect("Go", []));
        await Assert.That(recursive).IsNotNull();
        var module = RuntimeAnalysisCache.GetOrLower(domain);
        RuntimeAnalysisCache.ReplaceModule(domain, [.. module.Select(type => type.Name != "A" ? type : type with {
            Methods = [.. type.Methods!.Select(m => m.Name == "Close" ? m with { Body = recursive } : m)],
        })]);
        exec = new ModuleInstanceExecutor(domain);

        var owner = exec.Create("B", new Dictionary<string, object?>()).Instance!;
        var self = exec.Create("A", new Dictionary<string, object?>()).Instance!;
        await Assert.That(exec.Link("kids", owner, self).Succeeded).IsTrue();

        var result = exec.Invoke(self, "Go");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("depth exceeded");
    }
}