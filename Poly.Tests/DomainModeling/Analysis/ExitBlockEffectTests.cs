using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// The guide's effect table makes <c>invoke</c>, <c>create</c>, <c>create in</c> and
/// <c>for … invoke</c> action-only. An exit block inlines into every hop that leaves its stage,
/// so each of them in an exit block is rejected (DMEFF018) instead of letting a callee or a
/// create transition the instance behind the DMEFF017 rule.
/// </summary>
public class ExitBlockEffectTests {
    private const string Items = """
        Item: entity {
          Tag: Text optional
          Touch: action { assign Tag to "x" }
        }
        """;

    private static IReadOnlyList<Diagnostic> ExitEffectErrors(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        return result.Analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.ExitBlockEffectNotSupported)
            .ToList();
    }

    private static async Task AssertRejected<TEffect>(string dsl, string effectName) where TEffect : Effect {
        var errors = ExitEffectErrors(dsl);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains($"'{effectName}'");
        await Assert.That(errors[0].Message).Contains("exit block of stage 'A'");
        await Assert.That(errors[0].Message).Contains("action");
        await Assert.That(errors[0].Node is TEffect).IsTrue();
    }

    [Test]
    public async Task Analyze_InvokeInExitBlock_IsRejected() =>
        await AssertRejected<InvokeActionEffect>("""
            domain E1
            Z: entity {
              Tag: Text optional
              A: stage {
                exit { invoke Touch }
                Touch: action { assign Tag to "x" }
              }
            }
            """, "invoke");

    [Test]
    public async Task Analyze_CreateInExitBlock_IsRejected() =>
        await AssertRejected<CreateEntityInstance>("""
            domain E2
            Z: entity {
              A: stage { exit { create Item { } } }
            }
            """ + "\n" + Items, "create");

    [Test]
    public async Task Analyze_CreateInRelationshipInExitBlock_IsRejected() =>
        await AssertRejected<CreateEntityInRelationshipEffect>("""
            domain E3
            Z: entity {
              Items: many Item
              A: stage { exit { create in Items { } } }
            }
            """ + "\n" + Items, "create in");

    [Test]
    public async Task Analyze_ForInvokeInExitBlock_IsRejected() =>
        await AssertRejected<ForEachInvokeEffect>("""
            domain E4
            Z: entity {
              Items: many Item
              A: stage { exit { for Items as i invoke i.Touch } }
            }
            """ + "\n" + Items, "for … invoke");

    [Test]
    public async Task Analyze_InvokeNestedInIfInExitBlock_IsRejected() =>
        await AssertRejected<InvokeActionEffect>("""
            domain E5
            Z: entity {
              Tag: Text optional
              A: stage {
                exit { if (Tag is "a") { invoke Touch } }
                Touch: action { assign Tag to "x" }
              }
            }
            """, "invoke");

    [Test]
    public async Task Analyze_AssignAndTransitionInExitBlock_AreNotRejected() {
        var errors = ExitEffectErrors("""
            domain E6
            Z: entity {
              Tag: Text optional
              A: stage {
                exit {
                  assign Tag to "bye"
                  if (Tag is "a") { transition to B }
                }
              }
              B: stage { }
            }
            """);
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Analyze_InvokeInActionAndEntry_IsNotRejected() {
        var errors = ExitEffectErrors("""
            domain E7
            Z: entity {
              Tag: Text optional
              A: stage {
                entry { invoke Touch }
                Touch: action { assign Tag to "x" }
                Go: action { invoke Touch }
              }
            }
            """);
        await Assert.That(errors).IsEmpty();
    }
}