using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>#367 B-1: branch environments are joined (lub) into the environment after an
/// <c>if</c>. Each test reads the post-if environment through the value range of a later
/// <c>assign Total to Qty</c>.</summary>
public class ConditionalEffectJoinTests {
    private static EvolutionResult Parse(string dsl) {
        var changes = new PolyDslParser(dsl).Parse();
        var empty = DomainTestFactory.Create("_", [], []);
        return new DomainEvolution(empty).ApplyAndAnalyze(changes);
    }

    private static ValueRange? TotalRange(EvolutionResult result) {
        var entity = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Item");
        var action = entity.Actions.Single(a => a.Name == "Run");
        var inv = result.Analysis.GetActionInvariants(action);
        return inv!.StageContexts().Single().Postconditions
            .Last(p => p.TargetProperty == "Total").ValueRange;
    }

    private static string Item(string body, string extraProps = "") => $$"""
        domain Test
        Item: entity {
          Flag: Number range(0, 1)
          Qty: Number range(0, 100)
          Total: Number range(0, 30)
          Other: Number range(0, 10)
          {{extraProps}}
          Run: action {
            {{body}}
          }
        }
        """;

    [Test]
    public async Task BothArms_AssignDifferentLiterals_HullFeedsLaterEffect() {
        var result = Parse(Item("""
            if (Flag <= 0) { assign Qty to 5 } else { assign Qty to 25 }
            assign Total to Qty
            """));
        await Assert.That(result.Succeeded).IsTrue();
        var range = TotalRange(result);
        await Assert.That(range!.Min).IsEqualTo(5d);
        await Assert.That(range.Max).IsEqualTo(25d);
        // The hull [5, 25] is inside Total's range(0, 30): no warning.
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("can fall outside constraint range(0, 30)"))).IsFalse();
    }

    [Test]
    public async Task BothArms_AssignSameLiteral_FactSurvives() {
        var result = Parse(Item("""
            if (Flag <= 0) { assign Qty to 7 } else { assign Qty to 7 }
            assign Total to Qty
            """));
        var range = TotalRange(result);
        await Assert.That(range!.Min).IsEqualTo(7d);
        await Assert.That(range.Max).IsEqualTo(7d);
    }

    [Test]
    public async Task OneArmOnly_WithElse_FactDoesNotSurvive() {
        var result = Parse(Item("""
            if (Flag <= 0) { assign Qty to 5 } else { assign Other to 1 }
            assign Total to Qty
            """));
        var range = TotalRange(result);
        await Assert.That(range!.Min).IsEqualTo(0d);
        await Assert.That(range.Max).IsEqualTo(100d);
    }

    [Test]
    public async Task OneArmOnly_NoElse_ImplicitElseIsIncoming() {
        var result = Parse(Item("""
            if (Flag <= 0) { assign Qty to 5 }
            assign Total to Qty
            """));
        var range = TotalRange(result);
        await Assert.That(range!.Min).IsEqualTo(0d);
        await Assert.That(range.Max).IsEqualTo(100d);
    }

    [Test]
    public async Task UntouchedSymbol_KeepsIncomingPreconditionRefinement() {
        // Qty is narrowed to [0, 80] by the precondition; the arms touch only Other.
        var result = Parse("""
            domain Test
            Item: entity {
              Flag: Number range(0, 1)
              Qty: Number range(0, 100)
              Total: Number range(0, 100)
              Other: Number range(0, 10)
              Small: policy { Qty <= 80 }
              Run: action
                require Small
              {
                if (Flag <= 0) { assign Other to 1 } else { assign Other to 2 }
                assign Total to Qty
              }
            }
            """);
        var range = TotalRange(result);
        await Assert.That(range!.Min).IsEqualTo(0d);
        await Assert.That(range.Max).IsEqualTo(80d);
    }

    [Test]
    public async Task NestedIf_InnerJoinFlowsIntoOuterThenArm() {
        // inner: Qty ∈ {5} ∪ {10} → [5, 10]; outer else: 8. Outer join → [5, 10].
        var result = Parse(Item("""
            if (Flag <= 0) {
              if (Other <= 5) { assign Qty to 5 } else { assign Qty to 10 }
            } else { assign Qty to 8 }
            assign Total to Qty
            """));
        var range = TotalRange(result);
        await Assert.That(range!.Min).IsEqualTo(5d);
        await Assert.That(range.Max).IsEqualTo(10d);
    }

    [Test]
    public async Task ConditionRefinement_RangeHullAcrossArms_IsNotEmpty() {
        // then: Qty ∈ [0, 50]; else: Qty ∈ [51, 100]; hull is the full [0, 100], never Empty.
        var result = Parse(Item("""
            if (Qty <= 50) { assign Other to 1 } else { assign Other to 2 }
            assign Total to Qty
            """));
        var range = TotalRange(result);
        await Assert.That(range!.Min).IsEqualTo(0d);
        await Assert.That(range.Max).IsEqualTo(100d);
    }

    [Test]
    public async Task ArmAssignsOutsideRefinement_StaysSound() {
        // then-arm: Qty ∈ [0, 5] then `assign Qty to 10` — the arm's value 10 must still be
        // inside the post-if range (the narrowing merge alone would call the arm unreachable).
        var result = Parse(Item("""
            if (Qty <= 5) { assign Qty to 10 } else { assign Qty to 60 }
            assign Total to Qty
            """));
        var range = TotalRange(result);
        await Assert.That(range!.Min <= 10d).IsTrue();
        await Assert.That(range.Max >= 60d).IsTrue();
    }
}