using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// A hop assigns <c>CurrentStage</c> after the source stage's exit block is inlined, so an
/// exit-block transition whose target can differ from the target of a hop that leaves the
/// same stage is overridden. Analyze rejects it (DMEFF017); the same-target shape stays legal.
/// </summary>
public class ExitBlockTransitionTargetTests {
    private static IReadOnlyList<Diagnostic> ExitTargetErrors(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        return result.Analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.ExitBlockTransitionTargetOverridden)
            .ToList();
    }

    private static async Task AssertOneOverride(string dsl, string target, params string[] phrases) {
        var errors = ExitTargetErrors(dsl);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("the exit-block target is overridden");
        foreach (var phrase in phrases)
            await Assert.That(errors[0].Message).Contains(phrase);
        await Assert.That(errors[0].Node is StageTransitionEffect st && st.TargetStage.StageName == target)
            .IsTrue();
    }

    [Test]
    public async Task Analyze_ExitTransitionTargetDiffersFromHop_IsRejected() =>
        await AssertOneOverride("""
            domain D1
            Z: entity {
              A: stage {
                exit { transition to C }
                Go: action { transition to B }
              }
              B: stage { }
              C: stage { }
            }
            """, "C", "action 'Go' leaves 'A' for 'B'");

    [Test]
    public async Task Analyze_ExitTransitionInsideIfTargetDiffersFromHop_IsRejected() =>
        await AssertOneOverride("""
            domain D2
            Z: entity {
              Count: Number
              A: stage { Go: action { transition to B } }
              B: stage {
                exit { if (Count >= 1) { transition to C } }
                Leave: action { transition to D }
              }
              C: stage { }
              D: stage { }
            }
            """, "C", "action 'Leave' leaves 'B' for 'D'");

    [Test]
    public async Task Analyze_ExitTransitionVsEntityLevelHop_IsRejected() =>
        await AssertOneOverride("""
            domain D3
            Z: entity {
              Go: action { transition to B }
              A: stage { exit { transition to C } }
              B: stage { }
              C: stage { }
            }
            """, "C", "entity-level action 'Go' transitions to 'B' and can leave 'A' at run time");

    [Test]
    public async Task Analyze_ExitTransitionVsForBodyHop_IsRejected() =>
        await AssertOneOverride("""
            domain D4
            Parent: entity {
              Items: many Item
              A: stage {
                exit { transition to C }
                Go: action { for i in Items { transition to B } }
              }
              B: stage { }
              C: stage { }
            }
            Item: entity { }
            """, "C", "the transition inside the 'for' loop in action 'Go'");

    [Test]
    public async Task Analyze_TwoExitTransitionsWithDifferentTargets_AreRejected() {
        var errors = ExitTargetErrors("""
            domain D5
            Z: entity {
              A: stage {
                exit {
                  transition to B
                  transition to C
                }
                Go: action { transition to B }
              }
              B: stage { }
              C: stage { }
            }
            """);
        await Assert.That(errors.Count).IsEqualTo(2);
        foreach (var error in errors) {
            await Assert.That(error.Message).Contains("this exit block also transitions to");
            await Assert.That(error.Node is StageTransitionEffect).IsTrue();
        }
    }

    [Test]
    public async Task Analyze_ExitTransitionAfterEarlierHop_IsRejected() =>
        await AssertOneOverride("""
            domain D6
            Z: entity {
              A: stage {
                Go: action {
                  transition to B
                  transition to D
                }
              }
              B: stage { exit { transition to C } }
              C: stage { }
              D: stage { }
            }
            """, "C", "follows an earlier transition");

    [Test]
    public async Task Analyze_ExitTransitionAfterBranchHop_IsRejected() =>
        await AssertOneOverride("""
            domain D7
            Z: entity {
              Flag: Boolean
              A: stage {
                Go: action {
                  if (Flag is true) { transition to B }
                  transition to D
                }
              }
              B: stage { exit { transition to C } }
              C: stage { }
              D: stage { }
            }
            """, "C", "follows an earlier transition, branch, invoke or create");

    [Test]
    public async Task Analyze_ExitTransitionAfterInvoke_IsRejected() =>
        await AssertOneOverride("""
            domain D8
            Z: entity {
              Tag: Text
              A: stage {
                Touch: action { assign Tag to "x" }
                Go: action {
                  invoke Touch
                  transition to B
                }
              }
              B: stage { }
              C: stage { }
              D: stage { exit { transition to C } }
            }
            """, "C", "action 'Go' transitions to 'B' and can leave 'D' at run time");

    [Test]
    public async Task Analyze_ExitTransitionVsEntryBlockHop_IsRejected() =>
        await AssertOneOverride("""
            domain D9
            Z: entity {
              A: stage {
                entry { transition to B }
                exit { transition to C }
              }
              B: stage { }
              C: stage { }
            }
            """, "C", "the entry block of 'A' leaves 'A' for 'B'");

    private const string PeerDsl = """
        Paper: entity {
          A: stage { Advance: action { transition to B } }
          B: stage { }
        }
        """;

    [Test]
    public async Task Analyze_ExitTransitionVsStageWhenHop_IsRejected() =>
        await AssertOneOverride("domain D10\n" + PeerDsl + """

            Z: entity {
              Tracks: Paper
              A: stage {
                exit { transition to C }
                when Tracks B { transition to D }
              }
              C: stage { }
              D: stage { }
            }
            """, "C", "the 'when' subscription on 'A' leaves 'A' for 'D'");

    [Test]
    public async Task Analyze_ExitTransitionVsEntityWhenHop_IsRejected() =>
        await AssertOneOverride("domain D11\n" + PeerDsl + """

            Z: entity {
              Tracks: Paper
              when Tracks B { transition to D }
              A: stage { exit { transition to C } }
              C: stage { }
              D: stage { }
            }
            """, "C", "entity-level 'when' subscription transitions to 'D'");

    [Test]
    public async Task Analyze_ExitTransitionSameTargetAsHop_IsAccepted() {
        var errors = ExitTargetErrors("""
            domain G1
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
            """);
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Analyze_ExitTransitionVsEntityLevelHopWithSameTarget_IsAccepted() {
        var errors = ExitTargetErrors("""
            domain G2
            Z: entity {
              Go: action { transition to C }
              A: stage { exit { transition to C } }
              C: stage { }
            }
            """);
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Analyze_AssignBeforeFirstHop_StaysStatic() {
        var errors = ExitTargetErrors("""
            domain G3
            Z: entity {
              Tag: Text
              A: stage {
                exit { transition to C }
                Go: action {
                  assign Tag to "x"
                  transition to C
                }
              }
              C: stage { }
            }
            """);
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Analyze_HopOnOtherStage_DoesNotConflict() {
        var errors = ExitTargetErrors("""
            domain G4
            Z: entity {
              A: stage {
                exit { transition to C }
                Go: action { transition to C }
              }
              B: stage { Go: action { transition to D } }
              C: stage { }
              D: stage { }
            }
            """);
        await Assert.That(errors).IsEmpty();
    }

    private const string Smuggled = """
        domain F1
        Z: entity {
          A: stage {
            exit { invoke Touch }
            Touch: action { transition to C }
            Go: action { transition to B }
          }
          B: stage { }
          C: stage { }
        }
        """;

    [Test]
    public async Task Analyze_ExitTransitionViaInvokeTargetDiffersFromHop_IsRejected() {
        var errors = ExitTargetErrors(Smuggled);
        // First run (before DMEFF018) asserted one DMEFF017 and was red at 0; the ruling rejects the
        // exit-block invoke itself (action-only), so the shape now reports DMEFF018 instead.
        await Assert.That(errors).IsEmpty();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(Smuggled).Parse());
        var rejected = result.Analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.ExitBlockEffectNotSupported)
            .ToList();
        await Assert.That(rejected.Count).IsEqualTo(1);
        await Assert.That(rejected[0].Node is InvokeActionEffect).IsTrue();
    }

    private const string ItemDsl = """
        Item: entity {
          Tag: Text optional
          Touch: action { assign Tag to "x" }
        }
        """;

    private static Task AssertAfterMovedEffect(string name, string effect) =>
        AssertOneOverride($$"""
            domain {{name}}
            Z: entity {
              Items: many Item
              A: stage {
                Go: action {
                  {{effect}}
                  transition to B
                }
              }
              B: stage { }
              C: stage { }
              D: stage { exit { transition to C } }
            }
            """ + "\n" + ItemDsl, "C", "action 'Go' transitions to 'B' and can leave 'D' at run time",
            "(it follows an earlier transition, branch, invoke or create)");

    [Test]
    public async Task Analyze_ExitTransitionAfterCreate_IsRejected() =>
        await AssertAfterMovedEffect("M1", "create Item { }");

    [Test]
    public async Task Analyze_ExitTransitionAfterCreateInRelationship_IsRejected() =>
        await AssertAfterMovedEffect("M2", "create in Items { }");

    [Test]
    public async Task Analyze_ExitTransitionAfterForInvoke_IsRejected() =>
        await AssertAfterMovedEffect("M3", "for Items as i invoke i.Touch");

    [Test]
    public async Task Analyze_ElseBranchHopRestartsFromEntryState_StaysStatic() {
        var errors = ExitTargetErrors("""
            domain G5
            Z: entity {
              Flag: Boolean
              A: stage {
                Go: action {
                  if (Flag is true) { transition to B } else { transition to B }
                }
              }
              B: stage { }
              C: stage { }
              D: stage { exit { transition to C } }
            }
            """);
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Analyze_ExitTransitionInsideForInExitBlock_IsRejected() =>
        await AssertOneOverride("""
            domain D12
            Z: entity {
              Items: many Item
              A: stage {
                exit { for i in Items { transition to C } }
                Go: action { transition to B }
              }
              B: stage { }
              C: stage { }
            }
            Item: entity { }
            """, "C", "action 'Go' leaves 'A' for 'B'");

    [Test]
    public async Task Analyze_DiagnosticAllowance_CannotAcknowledgeDMEFF017() {
        string[] paths = ["D1", "D1/Z", "D1/Z/A", "D1/Z/A/exit", "D1/Z/A/C"];
        var session = SessionBuilder.CreateEmpty().LoadCore().LoadTemporal()
            .WithDiagnosticAllowances([.. paths.Select(path => new DiagnosticAllowance("DMEFF017", path, ["perl-1"]))])
            .Build();
        var changes = new PolyDslParser("""
            domain D1
            Z: entity {
              A: stage {
                exit { transition to C }
                Go: action { transition to B }
              }
              B: stage { }
              C: stage { }
            }
            """, session).Parse();
        var domain = new DomainEvolution(new Domain("_", [])).Apply(changes, session: session).Root;
        var errors = session.Analyze(domain).Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.ExitBlockTransitionTargetOverridden)
            .ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
    }

    [Test]
    public async Task Analyze_EntityLevelHopAfterEarlierHop_NamesTheEarlierTransition() =>
        await AssertOneOverride("""
            domain F4a
            Z: entity {
              Go: action {
                transition to B
                transition to D
              }
              A: stage { exit { transition to C } }
              B: stage { }
              C: stage { }
              D: stage { }
            }
            """, "C", "entity-level action 'Go' transitions to 'D' and can leave 'A' at run time (it follows an earlier transition");

    [Test]
    public async Task Analyze_SameTargetExitAndHop_AreNamedOncePerTarget() {
        var errors = ExitTargetErrors("""
            domain F4b
            Z: entity {
              A: stage {
                exit {
                  transition to B
                  transition to C
                }
                Go: action { transition to B }
              }
              B: stage { }
              C: stage { }
            }
            """);
        var onC = errors.Single(e => e.Node is StageTransitionEffect st && st.TargetStage.StageName == "C");
        await Assert.That(onC.Message).Contains("this exit block also transitions to 'B'");
        await Assert.That(onC.Message).DoesNotContain("action 'Go' leaves 'A' for 'B'");
    }
}