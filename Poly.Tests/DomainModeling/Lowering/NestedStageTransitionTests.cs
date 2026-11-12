using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Each transition hop exits its own source stage. Before the fix the lowering cursor was
/// static for the whole body: a nested hop inlined while the outer hop's cursor still held
/// the outer source (so the outer exit ran twice and the inner exit never), and the outer
/// hop then overwrote the nested target (so a later effect in the same body exited the
/// wrong stage). A <c>for</c> body that can transition has no static source either.
/// The state assertions are the oracle; the parity row only proves the two hosts stay in step.
/// </summary>
public class NestedStageTransitionTests {
    // Repro A: A -> B -> C entry chain, exit on A and B.
    private const string EntryChain = """
        domain Chain
        Z: entity {
          ACount: Number default(0)
          BCount: Number default(0)
          A: stage {
            exit { assign ACount to ACount + 1 }
            entry { transition to B }
          }
          B: stage {
            entry { transition to C }
            exit { assign BCount to BCount + 1 }
          }
          C: stage { }
        }
        """;

    // Repro B (A1): two hops in one action body; the second must exit C, not B.
    private const string TwoHops = """
        domain Cursor
        Z: entity {
          ACount: Number default(0)
          BCount: Number default(0)
          CCount: Number default(0)
          A: stage {
            exit { assign ACount to ACount + 1 }
            Go: action {
              transition to B
              transition to D
            }
          }
          B: stage {
            entry { transition to C }
            exit { assign BCount to BCount + 1 }
          }
          C: stage { exit { assign CCount to CCount + 1 } }
          D: stage { }
        }
        """;

    // A5: a for body that transitions, no entry/exit transition anywhere (no loop guard).
    private const string ForBody = """
        domain Loop
        Parent: entity {
          ACount: Number default(0)
          BCount: Number default(0)
          Items: many Item
          A: stage {
            exit { assign ACount to ACount + 1 }
            Spawn: action { create in Items { } }
            Go: action { for i in Items { transition to B } }
          }
          B: stage { exit { assign BCount to BCount + 1 } }
        }
        Item: entity { }
        """;

    [Test]
    public async Task Create_NestedEntryChain_ExitsEachStageOnce() {
        var outcomes = await ParityScenario.FromDsl(EntryChain, "NestedChain")
            .AssertAgree(side => side.Create("Z"));

        await Assert.That(outcomes[0].State["ACount"]).IsEqualTo("1");
        await Assert.That(outcomes[0].State["BCount"]).IsEqualTo("1");
        await Assert.That(outcomes[0].State["Stage"]).IsEqualTo("C");
    }

    [Test]
    public async Task Invoke_EffectAfterNestedTransition_ExitsInnermostStage() {
        var outcomes = await ParityScenario.FromDsl(TwoHops, "NestedCursor")
            .AssertAgree(side => {
                side.Create("Z");
                side.Invoke("Go");
            });

        await Assert.That(outcomes[1].State["ACount"]).IsEqualTo("1");
        await Assert.That(outcomes[1].State["BCount"]).IsEqualTo("1");
        await Assert.That(outcomes[1].State["CCount"]).IsEqualTo("1");
        await Assert.That(outcomes[1].State["Stage"]).IsEqualTo("D");
    }

    [Test]
    public async Task For_TransitionInBody_ExitsCurrentStageEachIteration() {
        var outcomes = await ParityScenario.FromDsl(ForBody, "ForEachHop")
            .AssertAgree(side => {
                side.Create("Parent");
                side.Invoke("Spawn");
                side.Invoke("Spawn");
                side.Invoke("Go");
            });

        await Assert.That(outcomes[3].State["ACount"]).IsEqualTo("1");
        await Assert.That(outcomes[3].State["BCount"]).IsEqualTo("1");
        await Assert.That(outcomes[3].State["Stage"]).IsEqualTo("B");
    }

    [Test]
    public async Task Invoke_PostLoopTransition_ExitsRuntimeStage() {
        const string dsl = """
            domain LoopThen
            Parent: entity {
              ACount: Number default(0)
              BCount: Number default(0)
              Items: many Item
              A: stage {
                exit { assign ACount to ACount + 1 }
                Spawn: action { create in Items { } }
                Go: action {
                  for i in Items { transition to B }
                  transition to C
                }
              }
              B: stage { exit { assign BCount to BCount + 1 } }
              C: stage { }
            }
            Item: entity { }
            """;
        var two = await ParityScenario.FromDsl(dsl, "LoopThenTwo")
            .AssertAgree(side => {
                side.Create("Parent");
                side.Invoke("Spawn");
                side.Invoke("Spawn");
                side.Invoke("Go");
            });
        await Assert.That(two[3].State["ACount"]).IsEqualTo("1");
        await Assert.That(two[3].State["BCount"]).IsEqualTo("2");
        await Assert.That(two[3].State["Stage"]).IsEqualTo("C");

        // Zero iterations: the loop never runs, so the post-loop hop sees the declaring stage.
        var zero = await ParityScenario.FromDsl(dsl, "LoopThenZero")
            .AssertAgree(side => {
                side.Create("Parent");
                side.Invoke("Go");
            });
        await Assert.That(zero[1].State["ACount"]).IsEqualTo("1");
        await Assert.That(zero[1].State["BCount"]).IsEqualTo("0");
        await Assert.That(zero[1].State["Stage"]).IsEqualTo("C");
    }

    [Test]
    public async Task Create_NestedEntryChain_SimulateAndPrintedAgree() {
        // Green on master by design: both hosts consume the one lowered tree. A guard, not the oracle.
        var outcomes = await ParityScenario.FromDsl(EntryChain, "NestedChainParity")
            .AssertAgree(side => side.Create("Z"));

        await Assert.That(outcomes[0].Success).IsTrue();
        await Assert.That(outcomes[0].State["Stage"]).IsEqualTo("C");
    }

    [Test]
    public async Task Invoke_EntityLevelTransitionIntoEntryChain_ExitsEachStageOnce() {
        // Green guard: the entity-level (unknown source) hop already emits a dynamic per-stage exit
        // on master; the fix makes the nested hop static. Same semantics, green on both.
        const string dsl = """
            domain EntityLevel
            Z: entity {
              ACount: Number default(0)
              BCount: Number default(0)
              Go: action { transition to B }
              A: stage { exit { assign ACount to ACount + 1 } }
              B: stage {
                entry { transition to C }
                exit { assign BCount to BCount + 1 }
              }
              C: stage { }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "EntityLevelChain")
            .AssertAgree(side => {
                side.Create("Z");
                side.Invoke("Go");
            });

        await Assert.That(outcomes[1].State["ACount"]).IsEqualTo("1");
        await Assert.That(outcomes[1].State["BCount"]).IsEqualTo("1");
        await Assert.That(outcomes[1].State["Stage"]).IsEqualTo("C");
    }
}
/// <summary>
/// A hop that may not run (inside a branch, or inside an invoked action) cannot leave a
/// static cursor behind: after the branch the instance may still be in the outer source.
/// The cursor goes dynamic (null, the per-stage exit switch) after any conditional that
/// contains a hop, and after a self-invoke.
/// </summary>
public class GuardedHopCursorTests {
    private const string GuardedEntryHop = """
        domain Gate
        Z: entity {
          Ready: Boolean
          ACount: Number default(0)
          BCount: Number default(0)
          CCount: Number default(0)
          DCount: Number default(0)
          A: stage {
            exit { assign ACount to ACount + 1 }
            Go: action {
              transition to B
              transition to D
            }
          }
          B: stage {
            entry { if (Ready is true) { transition to C } }
            exit { assign BCount to BCount + 1 }
          }
          C: stage { exit { assign CCount to CCount + 1 } }
          D: stage { exit { assign DCount to DCount + 1 } }
        }
        """;

    private const string GuardedBodyHop = """
        domain GateBody
        Z: entity {
          Ready: Boolean
          ACount: Number default(0)
          BCount: Number default(0)
          A: stage {
            exit { assign ACount to ACount + 1 }
            Go: action {
              if (Ready is true) { transition to B }
              transition to C
            }
          }
          B: stage { exit { assign BCount to BCount + 1 } }
          C: stage { }
        }
        """;

    private const string InvokedHop = """
        domain Caller
        Z: entity {
          ACount: Number default(0)
          BCount: Number default(0)
          A: stage {
            exit { assign ACount to ACount + 1 }
            Step: action { transition to B }
            Go: action {
              invoke Step
              transition to C
            }
          }
          B: stage { exit { assign BCount to BCount + 1 } }
          C: stage { }
        }
        """;

    [Test]
    public async Task Invoke_GuardedEntryHopThenHop_ExitsStageTheBranchLeft() {
        var held = await ParityScenario.FromDsl(GuardedEntryHop, "GuardedEntryHeld")
            .AssertAgree(side => {
                side.Create("Z", ("Ready", true));
                side.Invoke("Go");
            });
        // The guard fires: A -> B -> C, so B's own exit runs inside its entry. The second
        // hop must then exit C (never B again) — that is the F1 discriminator.
        await Assert.That(held[1].State["ACount"]).IsEqualTo("1");
        await Assert.That(held[1].State["BCount"]).IsEqualTo("1");
        await Assert.That(held[1].State["CCount"]).IsEqualTo("1");
        await Assert.That(held[1].State["Stage"]).IsEqualTo("D");

        var notHeld = await ParityScenario.FromDsl(GuardedEntryHop, "GuardedEntryNotHeld")
            .AssertAgree(side => {
                side.Create("Z", ("Ready", false));
                side.Invoke("Go");
            });
        // The guard does not fire: the second hop exits B, and C is never entered.
        await Assert.That(notHeld[1].State["ACount"]).IsEqualTo("1");
        await Assert.That(notHeld[1].State["BCount"]).IsEqualTo("1");
        await Assert.That(notHeld[1].State["CCount"]).IsEqualTo("0");
        await Assert.That(notHeld[1].State["Stage"]).IsEqualTo("D");
    }

    [Test]
    public async Task Invoke_BranchHopThenHop_ExitsStageTheGuardLeft() {
        var held = await ParityScenario.FromDsl(GuardedBodyHop, "GuardedBodyHeld")
            .AssertAgree(side => {
                side.Create("Z", ("Ready", true));
                side.Invoke("Go");
            });
        await Assert.That(held[1].State["ACount"]).IsEqualTo("1");
        await Assert.That(held[1].State["BCount"]).IsEqualTo("1");
        await Assert.That(held[1].State["Stage"]).IsEqualTo("C");

        // The branch did not run, so the instance is still in A and the second hop exits A.
        var notHeld = await ParityScenario.FromDsl(GuardedBodyHop, "GuardedBodyNotHeld")
            .AssertAgree(side => {
                side.Create("Z", ("Ready", false));
                side.Invoke("Go");
            });
        await Assert.That(notHeld[1].State["ACount"]).IsEqualTo("1");
        await Assert.That(notHeld[1].State["BCount"]).IsEqualTo("0");
        await Assert.That(notHeld[1].State["Stage"]).IsEqualTo("C");
    }

    [Test]
    public async Task Invoke_SelfInvokeThatTransitions_ThenHop_ExitsCalleeStage() {
        var outcomes = await ParityScenario.FromDsl(InvokedHop, "CallerHop")
            .AssertAgree(side => {
                side.Create("Z");
                side.Invoke("Go");
            });
        await Assert.That(outcomes[1].State["ACount"]).IsEqualTo("1");
        await Assert.That(outcomes[1].State["BCount"]).IsEqualTo("1");
        await Assert.That(outcomes[1].State["Stage"]).IsEqualTo("C");
    }
}