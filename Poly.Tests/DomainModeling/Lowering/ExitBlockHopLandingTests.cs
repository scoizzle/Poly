using Poly.Ast.Nodes;
using Poly.DomainModeling.Lowering;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// An exit-block hop whose target equals the leaving hop's target is redundant: the leaving
/// hop owns the stage assign, the target's entry effects and the notify, once each. A dynamic
/// hop runs at most one stage's exit.
/// </summary>
public class ExitBlockHopLandingTests {
    const string SameTargetModel = """
        domain H1
        Z: entity {
          Count: Number default(0)
          A: stage {
            exit { transition to B }
            Go: action { transition to B }
          }
          B: stage { entry { assign Count to Count + 1 } }
        }
        """;

    [Test]
    public async Task Invoke_ExitHopToSameTarget_EntersOnce() {
        var outcomes = await ParityScenario.FromDsl(SameTargetModel, "ExitHopEntersOnce")
            .AssertAgree(side => {
                side.Create("Z");
                side.Invoke("Go");
            });
        await Assert.That(outcomes[1].State["Stage"]).IsEqualTo("B");
        await Assert.That(outcomes[1].State["Count"]).IsEqualTo("1");
    }

    [Test]
    public async Task Invoke_ExitHopToSameTarget_NotifiesOnce() {
        const string dsl = """
            domain H2
            Z: entity {
              ws: many W via tracks
              A: stage {
                exit { transition to B }
                Go: action { transition to B }
              }
              B: stage { }
            }
            W: entity {
              Seen: Number default(0)
              tracks: Z
              S: stage { when tracks B { assign Seen to Seen + 1 } }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "ExitHopNotifiesOnce", simulateDoesNotMaintain: ["state:ws"])
            .AssertAgree(side => {
                var z = side.Create("Z");
                var w = side.Create("W", ("tracks", z));
                side.Use(z);
                side.Invoke("Go");
                side.Use(w);
            });
        await Assert.That(outcomes[4].State["Seen"]).IsEqualTo("1");
    }

    [Test]
    public async Task Invoke_ExitHopToSameTarget_WhenAllGateFiresOnce() {
        const string dsl = """
            domain H3
            Z: entity {
              board: Board via items
              A: stage {
                exit { transition to B }
                Go: action { transition to B }
              }
              B: stage { }
            }
            Board: entity {
              Fires: Number default(0)
              items: many Z
              when all items B { assign Fires to Fires + 1 }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "ExitHopWhenAllOnce")
            .AssertAgree(side => {
                var z = side.Create("Z");
                var board = side.Create("Board", ("items", new List<object?> { z }));
                side.Use(z);
                side.Invoke("Go");
                side.Use(board);
            });
        await Assert.That(outcomes[4].State["Fires"]).IsEqualTo("1");
    }

    [Test]
    public async Task Invoke_ExitHopToSameTarget_KeepsTheAutomaticLoopGuardClean() {
        const string dsl = """
            domain H4
            Z: entity {
              Ready: Boolean
              A: stage {
                exit { transition to B }
                Go: action { transition to B }
              }
              B: stage { entry { if (Ready is true) { transition to C } } }
              C: stage { }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "ExitHopLoopGuardClean")
            .AssertAgree(side => {
                side.Create("Z", ("Ready", true));
                side.Invoke("Go");
            });
        await Assert.That(outcomes[1].Success).IsTrue();
        await Assert.That(outcomes[1].State["Stage"]).IsEqualTo("C");
    }

    [Test]
    public async Task Invoke_EntityLevelHopToSameTarget_EntersOnce() {
        const string dsl = """
            domain H5
            Z: entity {
              Count: Number default(0)
              Go: action { transition to C }
              A: stage { exit { transition to C } }
              C: stage { entry { assign Count to Count + 1 } }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "EntityHopEntersOnce")
            .AssertAgree(side => {
                side.Create("Z");
                side.Invoke("Go");
            });
        await Assert.That(outcomes[1].State["Stage"]).IsEqualTo("C");
        await Assert.That(outcomes[1].State["Count"]).IsEqualTo("1");
    }

    [Test]
    public async Task Export_GuardedExitHop_ContributesNoDeadArm() {
        const string dsl = """
            domain H6
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
        var (domain, analysis) = EvolvedDomain.FromDsl(dsl);
        var types = DomainProgramProjection.ToSyntax(domain, analysis);
        var z = types.Single(t => t.Name == "Z");
        var leave = (MethodDefinitionNode)z.Methods!.Single(m => m.Name == ActionBodies.Prefix + "Leave");
        var nodes = Descendants(leave.Body!).ToList();

        var stageAssigns = nodes.OfType<Assignment>().Count(a =>
            a.Destination is Member { Value: ThisReference, MemberName: "CurrentStage" });
        await Assert.That(stageAssigns).IsEqualTo(1);

        var deadArms = nodes.OfType<IfStatement>().Count(i =>
            i.ElseBranch is null && i.ThenBranch is Block { Nodes.Count: 0 });
        await Assert.That(deadArms).IsEqualTo(0);
    }

    [Test]
    public async Task Invoke_EntityLevelHop_DoesNotRunALaterStagesExit() {
        const string dsl = """
            domain H7
            Z: entity {
              Tag: Text
              Go: action { transition to C }
              A: stage { exit { transition to C } }
              C: stage { exit { assign Tag to "c-exit" } }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "EntityHopNoLaterExit")
            .AssertAgree(side => {
                side.Create("Z", ("Tag", "t"));
                side.Invoke("Go");
            });
        await Assert.That(outcomes[1].State["Stage"]).IsEqualTo("C");
        await Assert.That(outcomes[1].State["Tag"]).IsEqualTo("t");
    }

    [Test]
    public async Task Invoke_EntityLevelHop_StillRunsTheSourceStagesExit() {
        const string dsl = """
            domain H8
            Z: entity {
              Tag: Text
              Go: action { transition to D }
              A: stage { exit { assign Tag to "a-exit" } }
              C: stage { exit { assign Tag to "c-exit" } }
              D: stage { }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "EntityHopSourceExit")
            .AssertAgree(side => {
                side.Create("Z", ("Tag", "t"));
                side.Invoke("Go");
            });
        await Assert.That(outcomes[1].State["Tag"]).IsEqualTo("a-exit");
    }

    [Test]
    public async Task Invoke_EffectsAfterExitHop_RunBeforeTheLanding() {
        const string dsl = """
            domain H10
            Z: entity {
              Count: Number default(1)
              A: stage {
                exit {
                  transition to B
                  assign Count to Count + 10
                }
                Go: action { transition to B }
              }
              B: stage { entry { assign Count to Count * 2 } }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "EffectsAfterExitHop")
            .AssertAgree(side => {
                side.Create("Z");
                side.Invoke("Go");
            });
        await Assert.That(outcomes[1].State["Count"]).IsEqualTo("22");
    }

    [Test]
    public async Task Lower_StageWhenDeadArm_DoesNotThrow() {
        const string dsl = """
            domain H11
            W: entity {
              zs: many Z via w
              Y: stage { }
            }
            Z: entity {
              w: W
              A: stage {
                exit { transition to B }
                Go: action { transition to B }
              }
              B: stage { }
              C: stage { when w Y { transition to E } }
              E: stage { }
            }
            """;
        var scenario = ParityScenario.FromDsl(dsl, "StageWhenDeadArm");
        var outcomes = await scenario.AssertAgree(side => {
            var w = side.Create("W");
            side.Create("Z", ("w", w));
            side.Invoke("Go");
        });
        await Assert.That(outcomes[2].State["Stage"]).IsEqualTo("B");
    }

    // A1 on the lowered AST. Derivation: the Go landing assigns ZStage.B once; the stage-when
    // handler for `w Y` lowers its dead per-stage arm for A, whose exit hop to B differs from
    // the handler's own hop target E, so it falls through and lands B once more (2). The base
    // also lands the nested hop inside Go (3); suppressing any hop would drop the dead arm (1).
    [Test]
    public async Task Lower_StageWhenDeadArm_KeepsTheDifferingHopFallThrough() {
        const string dsl = """
            domain H11b
            W: entity {
              zs: many Z via w
              Y: stage { }
            }
            Z: entity {
              w: W
              A: stage {
                exit { transition to B }
                Go: action { transition to B }
              }
              B: stage { }
              C: stage { when w Y { transition to E } }
              E: stage { }
            }
            """;
        var (domain, analysis) = EvolvedDomain.FromDsl(dsl);
        var z = DomainProgramProjection.ToSyntax(domain, analysis).Single(t => t.Name == "Z");
        var landings = z.Methods!.OfType<MethodDefinitionNode>()
            .Where(m => m.Body is not null)
            .SelectMany(m => Descendants(m.Body!))
            .OfType<Assignment>()
            .Count(a => a.Destination is Member { Value: ThisReference, MemberName: "CurrentStage" }
                && a.Value is Member { MemberName: "B" });
        await Assert.That(landings).IsEqualTo(2);
    }

    [Test]
    public async Task Invoke_EntityLevelHop_RunsTheSecondArmsExit() {
        const string dsl = """
            domain H12
            Z: entity {
              Tag: Text
              Go: action { transition to D }
              A: stage {
                exit { assign Tag to "a-exit" }
                ToC: action { transition to C }
              }
              C: stage { exit { assign Tag to "c-exit" } }
              D: stage { }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "SecondArmExit")
            .AssertAgree(side => {
                side.Create("Z", ("Tag", "t"));
                side.Invoke("ToC");
                side.Invoke("Go");
            });
        await Assert.That(outcomes[2].State["Stage"]).IsEqualTo("D");
        await Assert.That(outcomes[2].State["Tag"]).IsEqualTo("c-exit");
    }

    [Test]
    [Arguments(2L, "1")]
    [Arguments(0L, "1")]
    public async Task Invoke_GuardedExitHop_EntersTheTargetOnce(long count, string entries) {
        const string dsl = """
            domain H13
            Z: entity {
              Count: Number
              Entries: Number default(0)
              A: stage {
                exit { if (Count >= 1) { transition to B } }
                Go: action { transition to B }
              }
              B: stage { entry { assign Entries to Entries + 1 } }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, $"GuardedExitHop{count}")
            .AssertAgree(side => {
                side.Create("Z", ("Count", count));
                side.Invoke("Go");
            });
        await Assert.That(outcomes[1].State["Stage"]).IsEqualTo("B");
        await Assert.That(outcomes[1].State["Entries"]).IsEqualTo(entries);
    }

    [Test]
    [Arguments(true, "0")]
    [Arguments(false, "1")]
    public async Task Invoke_ExitConditionalWithOneSideSuppressed_ParityBothOutcomes(bool cond, string x) {
        const string dsl = """
            domain H14
            Z: entity {
              C: Boolean
              X: Number default(0)
              Count: Number default(0)
              A: stage {
                exit { if (C is true) { transition to B } else { assign X to 1 } }
                Go: action { transition to B }
              }
              B: stage { entry { assign Count to Count + 1 } }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, $"ExitIfElseSuppressed{cond}")
            .AssertAgree(side => {
                side.Create("Z", ("C", cond));
                side.Invoke("Go");
            });
        await Assert.That(outcomes[1].State["Stage"]).IsEqualTo("B");
        await Assert.That(outcomes[1].State["Count"]).IsEqualTo("1");
        await Assert.That(outcomes[1].State["X"]).IsEqualTo(x);
    }

    static IEnumerable<Node> Descendants(Node root) {
        yield return root;
        foreach (var child in root.Children) {
            if (child is null) continue;
            foreach (var d in Descendants(child))
                yield return d;
        }
    }
}