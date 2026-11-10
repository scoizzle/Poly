using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #367 A-6: a policy that relates two properties (<c>Start &lt; End</c>) is invisible to the
/// per-symbol lattice unless it is held as a relational constraint. These tests drive the
/// existing policy syntax (no new authoring form) and read the analysis verdicts.
/// </summary>
public class RelationalConstraintAnalysisTests {
    private static EvolutionResult Parse(string dsl) {
        var changes = new PolyDslParser(dsl).Parse();
        var empty = DomainTestFactory.Create("_", [], []);
        return new DomainEvolution(empty).ApplyAndAnalyze(changes);
    }

    private static bool Unsatisfiable(EvolutionResult r) => r.Analysis.Diagnostics.Any(d =>
        d.Severity == DiagnosticSeverity.Error && d.Message.Contains("unsatisfiable preconditions"));

    [Test]
    public async Task RelationContradictedByDeclaredRanges_IsUnsatisfiablePrecondition() {
        // Start >= 10 and End <= 5: `Start < End` can never hold, so the action can never run.
        var result = Parse("""
            domain Test
            Item: entity {
              Start: Number range(10, 20)
              End: Number range(0, 5)
              Ordered: policy { Start < End }
              Run: action
                require Ordered
              {
                assign Start to Start + 0
              }
            }
            """);
        await Assert.That(Unsatisfiable(result)).IsTrue();
        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task TwoContradictoryRelations_AreUnsatisfiablePrecondition() {
        // No ranges at all: `Start < End` and `Start > End` contradict each other jointly.
        var result = Parse("""
            domain Test
            Item: entity {
              Start: Number
              End: Number
              Before: policy { Start < End }
              After: policy { Start > End }
              Run: action
                require Before
                require After
              {
                assign Start to Start + 0
              }
            }
            """);
        await Assert.That(Unsatisfiable(result)).IsTrue();
    }

    [Test]
    public async Task RelationSatisfiableByRanges_ProducesNoDiagnostic() {
        var result = Parse("""
            domain Test
            Item: entity {
              Start: Number range(0, 10)
              End: Number range(5, 20)
              Ordered: policy { Start < End }
              Run: action
                require Ordered
              {
                assign Start to Start + 0
              }
            }
            """);
        await Assert.That(Unsatisfiable(result)).IsFalse();
    }

    [Test]
    public async Task RelationOverUnboundedProperties_IsUndecided_NotViolated() {
        // Fail closed: with no bounds the relation cannot be decided, so it must not be reported
        // as violated (nor assumed satisfied).
        var result = Parse("""
            domain Test
            Item: entity {
              Start: Number
              End: Number
              Ordered: policy { Start < End }
              Run: action
                require Ordered
              {
                assign Start to Start + 0
              }
            }
            """);
        await Assert.That(Unsatisfiable(result)).IsFalse();
    }

    [Test]
    public async Task RelationIsDroppedAfterTheReferencedPropertyIsAssigned_ArmStaysLive() {
        // After `assign Start to 100` the fact "End > Start" is stale. If it survived, refining by
        // `Start > End` would meet it into bottom and wrongly kill the arm, hiding the real
        // out-of-range assignment inside it.
        var result = Parse("""
            domain Test
            Item: entity {
              Start: Number
              End: Number
              Qty: Number range(0, 10)
              Ordered: policy { Start < End }
              Run: action
                require Ordered
              {
                assign Start to 100
                if (Start > End) { assign Qty to 99 }
              }
            }
            """);
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("violates constraint range(0, 10)"))).IsTrue();
    }
}