using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// F292: an exit block runs while CurrentStage is still the exiting stage, so a transition
/// inside it would exit that same stage again. Lowering inlined the exit into itself until the
/// exporter overflowed the stack (exit 134). The analyzer now rejects it before anything lowers,
/// so these rows never reach the exporter.
/// </summary>
public class ExitBlockTransitionTests {
    [Test]
    public async Task Analyze_TransitionInExitBlock_IsRejected() {
        const string dsl = """
            domain E1
            Z: entity {
              Tag: Text
              A: stage {
                exit { transition to C }
                Go: action { transition to B }
              }
              B: stage { }
              C: stage { }
            }
            """;
        await Assert.That(() => EvolvedDomain.FromDsl(dsl))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Stage 'Z.A' has a transition in its exit block");
    }

    [Test]
    public async Task Analyze_TransitionInIfInNonFirstStageExit_IsRejected() {
        const string dsl = """
            domain E2
            Z: entity {
              Count: Number
              A: stage { Go: action { transition to B } }
              B: stage {
                exit { if (Count >= 0) { transition to C } }
                Leave: action { transition to C }
              }
              C: stage { }
            }
            """;
        await Assert.That(() => EvolvedDomain.FromDsl(dsl))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Stage 'Z.B' has a transition in its exit block");
    }

    // The rule is narrow: an exit block without a transition still exports, compiles and agrees.
    [Test]
    public async Task Invoke_ExitBlockWithoutTransition_SimulateAndPrintedAgree() {
        const string dsl = """
            domain E3
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
