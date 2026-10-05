using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Automatic transitions in entry/exit (including nested in if) are allowed when every
/// cycle passes through a real guard. Unconditional cycles are Analyze errors. Lowering
/// inlines an exit once (no self-re-entry); simulate and printed share a loop-guard backstop.
/// </summary>
public class ExitBlockTransitionTests {
    [Test]
    public async Task Invoke_ExitTransition_SimulateAndPrintedAgree() {
        const string dsl = """
            domain E1
            Z: entity {
              Tag: Text
              A: stage {
                exit {
                  assign Tag to "bye"
                  transition to B
                }
                Go: action { transition to B }
              }
              B: stage { }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "ExitTransitionPlain")
            .AssertAgree(side => {
                side.Create("Z", ("Tag", "t"));
                side.Invoke("Go");
            });
        await Assert.That(outcomes[1].State["Stage"]).IsEqualTo("B");
        await Assert.That(outcomes[1].State["Tag"]).IsEqualTo("bye");
    }

    [Test]
    public async Task Invoke_ExitTransitionInsideIf_SimulateAndPrintedAgree() {
        const string dsl = """
            domain E2
            Z: entity {
              Count: Number
              Tag: Text
              A: stage { Go: action { transition to B } }
              B: stage {
                exit { if (Count >= 1) { assign Tag to "gated" } }
                Leave: action { transition to C }
              }
              C: stage { }
            }
            """;
        // Nested-if coverage on exit (assign, not transition) still exports; the transition
        // rows below cover guarded exit transitions.
        var baseOut = await ParityScenario.FromDsl(dsl, "ExitIfAssign")
            .AssertAgree(side => {
                side.Create("Z", ("Count", 2L), ("Tag", "t"));
                side.Invoke("Go");
                side.Invoke("Leave");
            });
        await Assert.That(baseOut[2].State["Tag"]).IsEqualTo("gated");

        const string exitTransitionDsl = """
            domain E2b
            Z: entity {
              Count: Number
              A: stage { Go: action { transition to B } }
              B: stage {
                exit { if (Count >= 1) { transition to C } }
                Leave: action { transition to C }
              }
              C: stage { }
            }
            """;
        var withTransition = await ParityScenario.FromDsl(exitTransitionDsl, "ExitIfTransition")
            .AssertAgree(side => {
                side.Create("Z", ("Count", 2L));
                side.Invoke("Go");
                side.Invoke("Leave");
            });
        await Assert.That(withTransition[2].State["Stage"]).IsEqualTo("C");
    }

    [Test]
    public async Task Analyze_UnconditionalAutomaticCycle_IsRejected() {
        const string dsl = """
            domain E3
            Z: entity {
              Tag: Text
              A: stage { entry { transition to B } }
              B: stage { entry { transition to A } }
            }
            """;
        await Assert.That(() => EvolvedDomain.FromDsl(dsl))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("unconditional automatic stage transition cycle");
    }

    [Test]
    public async Task Invoke_GatedAutomaticChain_AdvancesWhenConstraintHolds() {
        const string dsl = """
            domain E4
            Z: entity {
              Ready: Boolean
              A: stage { Go: action { transition to B } }
              B: stage { entry { if (Ready is true) { transition to C } } }
              C: stage { }
            }
            """;
        var held = await ParityScenario.FromDsl(dsl, "GateHeld")
            .AssertAgree(side => {
                side.Create("Z", ("Ready", true));
                side.Invoke("Go");
            });
        await Assert.That(held[1].State["Stage"]).IsEqualTo("C");

        var notHeld = await ParityScenario.FromDsl(dsl, "GateNot")
            .AssertAgree(side => {
                side.Create("Z", ("Ready", false));
                side.Invoke("Go");
            });
        await Assert.That(notHeld[1].State["Stage"]).IsEqualTo("B");
    }

    [Test]
    public async Task Invoke_GuardedCycleThatKeepsHolding_FailsLoudOnBothSides() {
        // Analyze allows this (every edge is guarded); the runtime loop guard stops it.
        const string dsl = """
            domain E6
            Z: entity {
              Ready: Boolean
              A: stage { Go: action { transition to B } }
              B: stage { entry { if (Ready is true) { transition to C } } }
              C: stage { entry { if (Ready is true) { transition to B } } }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "GuardedLoop")
            .AssertAgree(side => {
                side.Create("Z", ("Ready", true));
                side.Invoke("Go");
            });
        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].Message).Contains("Automatic stage transition loop on entity 'Z'");
    }

    [Test]
    public async Task Invoke_ExitBlockWithoutTransition_SimulateAndPrintedAgree() {
        const string dsl = """
            domain E5
            Z: entity {
              Tag: Text
              A: stage {
                exit { assign Tag to "left" }
                Go: action { transition to B }
              }
              B: stage { }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "ExitNoTransition")
            .AssertAgree(side => {
                side.Create("Z", ("Tag", "t"));
                side.Invoke("Go");
            });
        await Assert.That(outcomes[1].State["Stage"]).IsEqualTo("B");
        await Assert.That(outcomes[1].State["Tag"]).IsEqualTo("left");
    }
}
