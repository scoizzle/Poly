using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// C4e: Analyze rejects three unknown-name probes that previously passed with 0 errors.
/// </summary>
public class UnknownNameAnalysisTests {
    private static EvolutionResult Parse(string dsl) {
        var parser = new PolyDslParser(dsl);
        var changes = parser.Parse();
        var empty = DomainTestFactory.Create("_", [], []);
        return new DomainEvolution(empty).Apply(changes);
    }

    private static AnalysisResult Analyze(EvolutionResult result) =>
        result.Succeeded ? DomainModelAnalyzer.Analyze(result.Root!) : result.Analysis;

    private static bool HasError(AnalysisResult analysis, string code) =>
        analysis.Diagnostics.Any(d =>
            d.Code == code && d.Severity == DiagnosticSeverity.Error);

    // ── (a) unknown stage in a when-handler transition ─────────

    [Test]
    public async Task WhenHandler_TransitionToUnknownStage_ReportsError() {
        var analysis = Analyze(Parse("""
            domain Watch
            Paper: entity {
              Title: Text
              A: stage {
                Advance: action { transition to B }
              }
              B: stage { }
            }
            Tr: entity {
              Tracks: Paper
              P: stage {
                when Tracks B {
                  transition to Nope
                }
              }
              Q: stage { }
            }
            """));

        await Assert.That(HasError(analysis, DomainModelDiagnosticCodes.EffectBinding)).IsTrue();
    }

    [Test]
    public async Task WhenHandler_TransitionToKnownStage_NoEffectBindingError() {
        var analysis = Analyze(Parse("""
            domain Watch
            Paper: entity {
              Title: Text
              A: stage {
                Advance: action { transition to B }
              }
              B: stage { }
            }
            Tr: entity {
              Tracks: Paper
              P: stage {
                when Tracks B {
                  transition to Q
                }
              }
              Q: stage { }
            }
            """));

        await Assert.That(HasError(analysis, DomainModelDiagnosticCodes.EffectBinding)).IsFalse();
    }

    // ── (b) unknown enum member in a property default ──────────

    [Test]
    public async Task Default_UnknownEnumMember_ReportsError() {
        var analysis = Analyze(Parse("""
            domain Test
            Sev: enum { Low, High }
            Item: entity {
              Level: Sev default(Nope)
            }
            """));

        await Assert.That(HasError(analysis, DomainModelDiagnosticCodes.SemanticTypeCompatibility)).IsTrue();
    }

    [Test]
    public async Task Default_KnownEnumMember_NoTypeError() {
        var analysis = Analyze(Parse("""
            domain Test
            Sev: enum { Low, High }
            Item: entity {
              Level: Sev default(Low)
            }
            """));

        await Assert.That(HasError(analysis, DomainModelDiagnosticCodes.SemanticTypeCompatibility)).IsFalse();
    }

    // ── (c) unknown name in an `is` comparison ─────────────────

    [Test]
    public async Task IsComparison_UnknownEnumMember_ReportsError() {
        var analysis = Analyze(Parse("""
            domain Test
            Sev: enum { Low, High }
            Item: entity {
              Level: Sev
              Check: action {
                if (Level is Nope) {
                  assign Level to Low
                }
              }
            }
            """));

        await Assert.That(HasError(analysis, DomainModelDiagnosticCodes.SemanticTypeCompatibility)).IsTrue();
    }

    [Test]
    public async Task IsComparison_KnownEnumMember_NoTypeError() {
        var analysis = Analyze(Parse("""
            domain Test
            Sev: enum { Low, High }
            Item: entity {
              Level: Sev
              Check: action {
                if (Level is Low) {
                  assign Level to High
                }
              }
            }
            """));

        await Assert.That(HasError(analysis, DomainModelDiagnosticCodes.SemanticTypeCompatibility)).IsFalse();
    }
}