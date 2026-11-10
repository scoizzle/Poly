using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

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

    // A property assigned inside the `if` arm and read by a later assign makes arm liveness observable:
    // a dead arm is skipped by the join, so `Z` stays within range(0, 15); a live arm widens `Qty`
    // to [0, 25] and `assign Z to Qty + 10` can fall outside. (A literal assign inside the arm would
    // be flagged by literal validation whether or not the arm is live, so it cannot prove liveness.)
    private static bool ZCanFallOutside(EvolutionResult r, string? chain = null) => r.Analysis.Diagnostics.Any(d =>
        d.Severity == DiagnosticSeverity.Warning
        && d.Message.Contains("can fall outside constraint range(0, 15)")
        && (chain is null || d.Message.Contains(chain)));

    [Test]
    public async Task RelationIsDroppedAfterTheReferencedPropertyIsAssigned_ArmStaysLive() {
        // After `assign Start to 100` the fact "Start < End" is stale. If it survived, refining by
        // `Start > End` would meet it into bottom and wrongly kill the arm.
        var result = Parse("""
            domain Test
            Item: entity {
              Start: Number
              End: Number
              Qty: Number range(0, 30)
              Z: Number range(0, 15)
              Ordered: policy { Start < End }
              Small: policy { Qty <= 5 }
              Run: action
                require Ordered
                require Small
              {
                assign Start to 100
                if (Start > End) { assign Qty to 25 }
                assign Z to Qty + 10
              }
            }
            """);
        await Assert.That(ZCanFallOutside(result)).IsTrue();
    }

    [Test]
    public async Task RelationKeptWhenNothingIsAssigned_ContradictingArmIsDead() {
        // Positive twin: with no reassignment `Start < End` holds, so `Start > End` is unreachable
        // and nothing assigned inside it reaches the later assign.
        var result = Parse("""
            domain Test
            Item: entity {
              Start: Number
              End: Number
              Qty: Number range(0, 30)
              Z: Number range(0, 15)
              Ordered: policy { Start < End }
              Small: policy { Qty <= 5 }
              Run: action
                require Ordered
                require Small
              {
                if (Start > End) { assign Qty to 25 }
                assign Z to Qty + 10
              }
            }
            """);
        await Assert.That(ZCanFallOutside(result)).IsFalse();
    }

    [Test]
    public async Task RelationIsDroppedAfterACalleeAssignsTheOtherProperty_ArmStaysLive() {
        // F1(i): the callee runs on a clone, so the caller's `Start < End` would survive a callee
        // that reassigns End. Resolution: all relations are dropped after an invoke.
        var result = Parse("""
            domain Test
            Item: entity {
              Start: Number
              End: Number
              Qty: Number range(0, 30)
              Z: Number range(0, 15)
              Ordered: policy { Start < End }
              Small: policy { Qty <= 5 }
              Widen: action { assign End to 0 }
              Run: action
                require Ordered
                require Small
              {
                invoke Widen
                if (Start > End) { assign Qty to 25 }
                assign Z to Qty + 10
              }
            }
            """);
        await Assert.That(ZCanFallOutside(result)).IsTrue();
    }

    [Test]
    public async Task RelationIsDroppedAfterAForEachInvokeWhoseCalleeReassignsTheOtherProperty_ArmStaysLive() {
        // The for-each path drops relations on its own (separate from the plain invoke): Ship's
        // callee chain (Line.Touch -> Order.Reset) reassigns End, so `Start < End` is stale and the
        // contradicting arm must stay live.
        var result = Parse("""
            domain Test
            Order: entity {
              Start: Number
              End: Number
              Qty: Number range(0, 30)
              Z: Number range(0, 15)
              lines: many Line
              Ordered: policy { Start < End }
              Small: policy { Qty <= 5 }
              Reset: action { assign End to 0 }
              Ship: action
                require Ordered
                require Small
              {
                for lines as line invoke line.Touch
                if (Start > End) { assign Qty to 25 }
                assign Z to Qty + 10
              }
            }
            Line: entity {
              order: Order
              Touch: action { invoke order.Reset }
            }
            """);
        await Assert.That(ZCanFallOutside(result)).IsTrue();
    }

    [Test]
    public async Task StaleRelationDoesNotResurrectThroughABoundParameter() {
        // F1(ii): End is read with its relation to Start and bound to a parameter; the callee then
        // reassigns Start and copies the parameter. The parameter's relation must not reappear.
        var result = Parse("""
            domain Test
            Item: entity {
              Start: Number
              End: Number
              Mirror: Number range(0, 30)
              Qty: Number range(0, 30)
              Z: Number range(0, 15)
              Ordered: policy { Start < End }
              Small: policy { Qty <= 5 }
              Take: action (x: Number) {
                assign Start to 100
                assign Mirror to x
                if (Mirror <= Start) { assign Qty to 25 }
                assign Z to Qty + 10
              }
              Run: action
                require Ordered
                require Small
              {
                invoke Take(x: End)
              }
            }
            """);
        await Assert.That(ZCanFallOutside(result, "Run → Take")).IsTrue();
    }

    [Test]
    public async Task CallerRelationDoesNotFlowIntoAnotherEntityThroughABinding() {
        // F1(iii): relations are keyed by property name; the caller's `Start < End` must not meet the
        // other entity's own Start.
        var result = Parse("""
            domain Test
            Order: entity {
              Start: Number
              End: Number
              lines: many Line
              Ordered: policy { Start < End }
              Ship: action
                require Ordered
              {
                for lines as line invoke line.Take(x: End)
              }
            }
            Line: entity {
              Start: Number range(0, 100)
              Mirror: Number range(0, 30)
              Qty: Number range(0, 30)
              Z: Number range(0, 15)
              Small: policy { Qty <= 5 }
              order: Order
              Take: action (x: Number)
                require Small
              {
                assign Mirror to x
                if (Mirror <= Start) { assign Qty to 25 }
                assign Z to Qty + 10
              }
            }
            """);
        await Assert.That(ZCanFallOutside(result, "Ship → Take")).IsTrue();
    }

    [Test]
    public async Task AssigningOnePropertyFromTheOther_DropsTheStaleRelation_ArmStaysLive() {
        // After `assign Start to End` the fact "Start < End" is stale (Start now equals End), so the
        // contradicting arm below stays live. (A Start-vs-Start self-relation would be inert, so the
        // no-relations-on-read guarantee itself is pinned by EvalOfAProperty_ReturnsNoRelations.)
        var result = Parse("""
            domain Test
            Item: entity {
              Start: Number
              End: Number
              Qty: Number range(0, 30)
              Z: Number range(0, 15)
              Ordered: policy { Start < End }
              Small: policy { Qty <= 5 }
              Run: action
                require Ordered
                require Small
              {
                assign Start to End
                if (Start > End) { assign Qty to 25 }
                assign Z to Qty + 10
              }
            }
            """);
        await Assert.That(ZCanFallOutside(result)).IsTrue();
    }

    [Test]
    public async Task EvalOfAProperty_ReturnsNoRelations() {
        var start = AbstractValue.From([new RangeConstraint(0d, 10d), new RelationalConstraint("End", RelationalOrdering.Less)]);
        var env = new Dictionary<string, AbstractValue> { ["Start"] = start };
        var entity = new Entity("Item", [], [], [], []);
        var v = EffectInvariantAnalyzer.Eval(DomainExpression.Property("Start"), entity, null, env);
        await Assert.That(v.Constraints.OfType<RelationalConstraint>().Any()).IsFalse();
        await Assert.That(v.Constraints.OfType<RangeConstraint>().Any()).IsTrue();
    }

    [Test]
    public async Task NarrowingAfterABottom_StaysUnsatisfiable() {
        // F2: P1 and P2 contradict; a later restatement (P3) must not erase the bottom.
        var bottom = AbstractValue.From([new RangeConstraint(0d, 5d)]).Narrow(new RangeConstraint(10d, null));
        await Assert.That(bottom.Unsatisfiable).IsTrue();
        var later = bottom.Narrow(new RangeConstraint(null, 100d));
        await Assert.That(later.Unsatisfiable).IsTrue();
        var viaNewKind = bottom.Narrow(new EqualityConstraint("x"));
        await Assert.That(viaNewKind.Unsatisfiable).IsTrue();
    }

    [Test]
    public async Task MergingAfterABottom_StaysUnsatisfiable() {
        // S2: Merge keeps bottom from either side, including via the shortcut paths.
        var bottom = AbstractValue.From([new RangeConstraint(0d, 5d)]).Narrow(new RangeConstraint(10d, null));
        await Assert.That(bottom.Unsatisfiable).IsTrue();
        var plain = AbstractValue.From([new RangeConstraint(0d, 100d)]);
        await Assert.That(bottom.Merge(plain).Unsatisfiable).IsTrue();
        await Assert.That(plain.Merge(bottom).Unsatisfiable).IsTrue();
        await Assert.That(bottom.Merge(AbstractValue.Unknown).Unsatisfiable).IsTrue();
        await Assert.That(AbstractValue.Unknown.Merge(bottom).Unsatisfiable).IsTrue();
        await Assert.That(bottom.Merge(AbstractValue.From([new EqualityConstraint("x")])).Unsatisfiable).IsTrue();
    }

    [Test]
    public async Task ContradictionThenRestatement_IsStillUnsatisfiablePrecondition() {
        var result = Parse("""
            domain Test
            Item: entity {
              Start: Number
              End: Number
              Before: policy { Start < End }
              After: policy { Start > End }
              Again: policy { Start < End }
              Run: action
                require Before
                require After
                require Again
              {
                assign Start to Start + 0
              }
            }
            """);
        await Assert.That(Unsatisfiable(result)).IsTrue();
    }
}