using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Parsing;

namespace Poly.Tests.DomainModeling.Analysis;

public class ConstraintPropagationEffectTests {
    private static EvolutionResult Parse(string dsl) {
        var parser = new PolyDslParser(dsl);
        var changes = parser.Parse();
        var empty = DomainTestFactory.Create("_", [], []);
        return new DomainEvolution(empty).Apply(changes);
    }

    private static IEnumerable<string> Messages(EvolutionResult result, bool errors = true) =>
        result.Analysis.Diagnostics
            .Where(d => errors ? d.Severity == DiagnosticSeverity.Error : d.Severity == DiagnosticSeverity.Warning)
            .Select(d => d.Message);

    [Test]
    public async Task Assign_LiteralOutOfRange_IsError() {
        var result = Parse("""
            domain Test
            Person: entity {
              Age: Number range(0, 150)
              Set: action { assign Age to 200 }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Messages(result).Any(m => m.Contains("violates constraint range(0, 150)"))).IsTrue();
    }

    [Test]
    public async Task Assign_DerivedEntirelyOutOfRange_IsError() {
        // Age range(0,150); Age + 200 is always in [200, 350] — definite violation.
        var result = Parse("""
            domain Test
            Person: entity {
              Age: Number range(0, 150)
              Set: action { assign Age to Age + 200 }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Messages(result).Any(m => m.Contains("is entirely outside constraint range(0, 150)"))).IsTrue();
    }

    [Test]
    public async Task Assign_DerivedCanFallOutside_IsWarning() {
        // Qty range(0,100); Qty - 100 is in [-100, 0] — can violate the lower bound.
        var result = Parse("""
            domain Test
            Item: entity {
              Qty: Number range(0, 100)
              Dec: action { assign Qty to Qty - 100 }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(Messages(result, errors: false).Any(m => m.Contains("can fall outside constraint range(0, 100)"))).IsTrue();
    }

    [Test]
    public async Task Assign_DerivedWithinRange_NoDiagnostic() {
        // Qty range(0,80); Total range(0,200); Qty + 10 is in [10, 90] — fully within.
        var result = Parse("""
            domain Test
            Item: entity {
              Qty: Number range(0, 80)
              Total: Number range(0, 200)
              Inc: action { assign Total to Qty + 10 }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(Messages(result, errors: false)).IsEmpty();
    }

    [Test]
    public async Task EntryEffect_DerivedOutOfRange_IsError() {
        // Entry effects flow through the same assign validation.
        var result = Parse("""
            domain Test
            Item: entity {
              Score: Number range(0, 100)
              Active: stage {
                entry { assign Score to Score + 500 }
              }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Messages(result).Any(m => m.Contains("is entirely outside constraint range(0, 100)"))).IsTrue();
    }

    [Test]
    public async Task CreateIn_DerivedOutOfRange_IsError() {
        // A create-in initializer with a derived value outside the target's range.
        var result = Parse("""
            domain Test
            Order: entity {
              Total: Number range(0, 1000)
              lines: many OrderLine
              Add: action { create in lines { Total: Total + 5000 } }
            }
            OrderLine: entity {
              Total: Number range(0, 1000)
              order: Order
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Messages(result).Any(m => m.Contains("is entirely outside constraint range(0, 1000)"))).IsTrue();
    }

    [Test]
    public async Task Assign_GuardPolicyNarrowsRange_NoFalsePositiveWarning() {
        // Additive invariant consideration: `require LowQty (Qty <= 80)` is an additional
        // constraint — when Inc runs, Qty is provably in [0, 80], so Qty + 10 is in [10, 90]
        // and cannot exceed the target's range(0, 90). Without the policy, the naive
        // [10, 100] range would emit a false-positive warning.
        var result = Parse("""
            domain Test
            Item: entity {
              Qty: Number range(0, 90)
              LowQty: policy { Qty <= 80 }
              Active: stage {
                Inc: action
                  require LowQty
                {
                  assign Qty to Qty + 10
                }
              }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(Messages(result, errors: false)).IsEmpty();
    }

    [Test]
    public async Task Assign_GuardPolicyDoesNotPreventViolation_StillRejected() {
        // Qty <= 80 still leaves Qty + 100 entirely outside range(0, 90) — the narrowing
        // does not hide a real violation (it's a definite error, not just a warning).
        var result = Parse("""
            domain Test
            Item: entity {
              Qty: Number range(0, 90)
              LowQty: policy { Qty <= 80 }
              Active: stage {
                Inc: action
                  require LowQty
                {
                  assign Qty to Qty + 100
                }
              }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Messages(result).Any(m => m.Contains("is entirely outside constraint range(0, 90)"))).IsTrue();
    }

    [Test]
    public async Task Assign_EntityLevelPolicyNarrowsRange_NoWarning() {
        // Entity-level policies are always-on gates — they narrow the range too.
        var result = Parse("""
            domain Test
            Item: entity {
              Qty: Number range(0, 90)
              LowQty: policy { Qty <= 80 }
              Inc: action { assign Qty to Qty + 10 }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(Messages(result, errors: false)).IsEmpty();
    }

    [Test]
    public async Task Action_ValidInMultipleStages_PerStageContextsAndCombinedRanges() {
        // An entity-level action is valid in every stage; each stage's policies are an
        // additional constraint (stage policies are model-level, populated programmatically
        // here since the DSL does not yet author them). Stage A narrows Qty to [0,80],
        // stage B to [0,60] — the per-stage contexts must differ, and the combined view
        // must take the stricter maximum (60).
        var qty = new Property("Qty", new DomainTypeReference("Number"),
            [new RangeConstraint(0d, 90d)]);
        var inc = new Poly.DomainModeling.Action("Inc", InvocationResult.Void, [], [
            new AssignEffect(DomainExpression.Property("Qty"),
                DomainExpression.Add(DomainExpression.Property("Qty"), DomainExpression.Literal(10L)))
        ], []);
        var a = new Stage("A",
            Actions: [], Policies: [
                new Policy("A80", DomainExpression.LessThanOrEqual(
                    DomainExpression.Property("Qty"), DomainExpression.Literal(80)))
            ], OnEntryEffects: [], OnExitEffects: []);
        var b = new Stage("B",
            Actions: [], Policies: [
                new Policy("B60", DomainExpression.LessThanOrEqual(
                    DomainExpression.Property("Qty"), DomainExpression.Literal(60)))
            ], OnEntryEffects: [], OnExitEffects: []);
        var entity = new Entity("Item", [qty], [inc], [], [a, b]);
        var domain = DomainTestFactory.Create("Test", [entity], []);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var invariants = analysis.GetActionInvariants(inc);
        await Assert.That(invariants).IsNotNull();

        var contexts = invariants!.StageContexts();
        await Assert.That(contexts.Count).IsEqualTo(2);
        var ctxA = contexts.Single(c => c.StageName == "A");
        var ctxB = contexts.Single(c => c.StageName == "B");
        await Assert.That(ctxA.NarrowedRanges["Qty"]!.Max).IsEqualTo(80);
        await Assert.That(ctxB.NarrowedRanges["Qty"]!.Max).IsEqualTo(60);

        var combined = invariants.CombinedRanges();
        await Assert.That(combined["Qty"].Max).IsEqualTo(60);
    }

    [Test]
    public async Task ContradictoryEqualityConstraints_IsError() {
        // Two equality constraints with different values make the property unsatisfiable.
        var entity = new Entity("Item",
            [new Property("Kind", new DomainTypeReference("Text"),
                [new EqualityConstraint("a"), new EqualityConstraint("b")])],
            [], [], []);
        var domain = DomainTestFactory.Create("Test", [entity], []);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Message.Contains("contradictory EqualityConstraints"))).IsTrue();
    }

    [Test]
    public async Task DuplicateEqualEqualityConstraints_NoError() {
        // Two equality constraints with the SAME value are not contradictory.
        var entity = new Entity("Item",
            [new Property("Kind", new DomainTypeReference("Text"),
                [new EqualityConstraint("a"), new EqualityConstraint("a")])],
            [], [], []);
        var domain = DomainTestFactory.Create("Test", [entity], []);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Message.Contains("contradictory EqualityConstraints"))).IsFalse();
    }

    [Test]
    public async Task EqualityOutsideRange_IsError() {
        // An equality value outside the declared range is unsatisfiable.
        var entity = new Entity("Item",
            [new Property("N", new DomainTypeReference("Number"),
                [new EqualityConstraint(5L), new RangeConstraint(10d, 20d)])],
            [], [], []);
        var domain = DomainTestFactory.Create("Test", [entity], []);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Message.Contains("is outside its RangeConstraint"))).IsTrue();
    }

    [Test]
    public async Task Action_MultiStage_ViolationInOneStage_IsReported() {
        // `Qty + 30`: in stage A (Qty <= 80) the range [30, 110] can exceed range(0, 90);
        // in stage B (Qty <= 60) it stays within. The stricter stage's constraint must be
        // respected — a warning is reported because one state can violate.
        var qty = new Property("Qty", new DomainTypeReference("Number"),
            [new RangeConstraint(0d, 90d)]);
        var inc = new Poly.DomainModeling.Action("Inc", InvocationResult.Void, [], [
            new AssignEffect(DomainExpression.Property("Qty"),
                DomainExpression.Add(DomainExpression.Property("Qty"), DomainExpression.Literal(30L)))
        ], []);
        var a = new Stage("A",
            Actions: [], Policies: [
                new Policy("A80", DomainExpression.LessThanOrEqual(
                    DomainExpression.Property("Qty"), DomainExpression.Literal(80)))
            ], OnEntryEffects: [], OnExitEffects: []);
        var b = new Stage("B",
            Actions: [], Policies: [], OnEntryEffects: [], OnExitEffects: []);
        var entity = new Entity("Item", [qty], [inc], [], [a, b]);
        var domain = DomainTestFactory.Create("Test", [entity], []);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning
            && d.Message.Contains("can fall outside constraint range(0, 90)"))).IsTrue();
    }

    [Test]
    public async Task ContradictoryRanges_AreError() {
        // Two disjoint ranges on one property are jointly unsatisfiable.
        var entity = new Entity("Item",
            [new Property("N", new DomainTypeReference("Number"),
                [new RangeConstraint(0d, 5d), new RangeConstraint(10d, 20d)])],
            [], [], []);
        var analysis = DomainModelAnalyzer.Analyze(DomainTestFactory.Create("Test", [entity], []));
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Message.Contains("contradictory range(0, 5) constraints"))).IsTrue();
    }

    [Test]
    public async Task EqualityValueViolatesLength_IsError() {
        var entity = new Entity("Item",
            [new Property("Code", new DomainTypeReference("Text"),
                [new EqualityConstraint("abc"), new LengthConstraint(2, 2)])],
            [], [], []);
        var analysis = DomainModelAnalyzer.Analyze(DomainTestFactory.Create("Test", [entity], []));
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Message.Contains("EqualityConstraint value 'abc' violates length(2, 2)"))).IsTrue();
    }

    [Test]
    public async Task DefaultValueViolatesPattern_IsError() {
        var entity = new Entity("Item",
            [new Property("Code", new DomainTypeReference("Text"),
                [new DefaultValueConstraint(DomainExpression.Literal("abc")),
                 new PatternConstraint("^[0-9]+$")])],
            [], [], []);
        var analysis = DomainModelAnalyzer.Analyze(DomainTestFactory.Create("Test", [entity], []));
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Message.Contains("default value 'abc' violates pattern"))).IsTrue();
    }

    [Test]
    public async Task IfCondition_ImplicitPrecondition_NarrowsBranchRange() {
        // `if (Qty >= 10) { assign Qty to Qty - 5 }` runs with Qty ∈ [10, 90], so
        // Qty - 5 ∈ [5, 85] — within range(0, 90). Without the condition-as-precondition
        // narrowing, the naive [−5, 85] range would warn.
        var result = Parse("""
            domain Test
            Item: entity {
              Qty: Number range(0, 90)
              Active: stage {
                Dec: action {
                  if (Qty >= 10) { assign Qty to Qty - 5 }
                }
              }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(Messages(result, errors: false)).IsEmpty();
    }

    [Test]
    public async Task IfCondition_ElseBranch_UsesNegatedCondition() {
        // `else` runs under the negation: Qty > 5 (from `if Qty <= 5`), so `Qty - 20`
        // ∈ [−15, 70] can go below 0 → warning on the else-branch. The then-branch
        // (Qty ∈ [0,5] → Qty + 10 ∈ [10, 15]) stays clean.
        var result = Parse("""
            domain Test
            Item: entity {
              Qty: Number range(0, 90)
              Active: stage {
                Dec: action {
                  if (Qty <= 5) { assign Qty to Qty + 10 }
                  else { assign Qty to Qty - 20 }
                }
              }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(Messages(result, errors: false).Any(m => m.Contains("can fall outside constraint range(0, 90)"))).IsTrue();
    }

    [Test]
    public async Task CrossEntityInvoke_WhereFilter_NarrowsCalleeContext() {
        // `invoke all lines.Mark where Qty <= 40` runs OrderLine.Mark (assign Qty to Qty + 10)
        // with Qty ∈ [0, 40] (declared range(0,100) ∩ filter ≤ 40), so the callee's postcondition
        // is [10, 50]. Order's invariants must carry the cross-entity call-chain postcondition
        // with the filtered range (not the unfiltered [10, 110]).
        var result = Parse("""
            domain Test
            Order: entity {
              lines: many OrderLine
              Ship: action { invoke all lines.Mark where Qty <= 40 }
            }
            OrderLine: entity {
              Qty: Number range(0, 100)
              order: Order
              Mark: action { assign Qty to Qty + 10 }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();

        var order = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var line = result.Root.Types.OfType<Entity>().Single(e => e.Name == "OrderLine");
        var ship = order.Actions.Single(a => a.Name == "Ship");
        var mark = line.Actions.Single(a => a.Name == "Mark");

        var invariants = result.Analysis.GetActionInvariants(ship);
        await Assert.That(invariants).IsNotNull();
        var post = invariants!.StageContexts().Single().Postconditions
            .SingleOrDefault(p => ReferenceEquals(p.DeclaringAction, mark));
        await Assert.That(post).IsNotNull();
        await Assert.That(post!.ValueRange!.Max).IsEqualTo(50d);
    }

    [Test]
    public async Task UnsatisfiablePreconditions_AreError() {
        // `require LowQty (Qty <= 80)` on `Qty range(90, 100)`: the guards narrow Qty to an
        // empty range — the action can never run.
        var result = Parse("""
            domain Test
            Item: entity {
              Qty: Number range(90, 100)
              LowQty: policy { Qty <= 80 }
              Active: stage {
                Inc: action
                  require LowQty
                {
                  assign Qty to Qty + 1
                }
              }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Messages(result).Any(m => m.Contains("unsatisfiable preconditions"))).IsTrue();
    }

    [Test]
    public async Task CallChain_InvokeCarriesCallerContextIntoCallee() {
        // A invokes B inside `if (Qty <= 60)`. The call-chain context narrows Qty to [0, 60]
        // (if-condition ∩ entity LowQty ≤ 80), so B's `Qty + 10` under A = [10, 70]; B's own
        // direct context (entity policy only) gives [10, 90]. A's invariants must carry the
        // call-chain-narrowed postcondition.
        var result = Parse("""
            domain Test
            Item: entity {
              Qty: Number range(0, 90)
              LowQty: policy { Qty <= 80 }
              B: action { assign Qty to Qty + 10 }
              A: action {
                if (Qty <= 60) { invoke B }
              }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();

        var entity = result.Root.Types.OfType<Entity>().Single(e => e.Name == "Item");
        var a = entity.Actions.Single(x => x.Name == "A");
        var b = entity.Actions.Single(x => x.Name == "B");
        var aInvariants = result.Analysis.GetActionInvariants(a);
        var bInvariants = result.Analysis.GetActionInvariants(b);
        await Assert.That(aInvariants).IsNotNull();
        await Assert.That(bInvariants).IsNotNull();

        // B's own postcondition (direct): wide [10, 90].
        var bPost = bInvariants!.StageContexts().Single().Postconditions.Single();
        await Assert.That(bPost.ValueRange!.Max).IsEqualTo(90d);

        // A's call-chain postcondition for the same B assign: narrowed [10, 70].
        var aPost = aInvariants!.StageContexts().Single().Postconditions
            .Single(p => ReferenceEquals(p.Effect, bPost.Effect));
        await Assert.That(aPost.ValueRange!.Max).IsEqualTo(70d);
    }

    [Test]
    public async Task CallChain_InvokeArgumentBinding_FlowsParamRange() {
        // A invokes `B(x: amount)` where amount range(0, 200) and B does
        // `assign Total to x` with Total range(0, 100). The chained postcondition's net
        // constraint is the intersection range(0, 100), and its value range is [0, 200].
        var qty = new Property("Qty", new DomainTypeReference("Number"), []);
        var total = new Property("Total", new DomainTypeReference("Number"),
            [new RangeConstraint(0d, 100d)]);
        var b = new Poly.DomainModeling.Action("B", InvocationResult.Void,
            [new Property("x", new DomainTypeReference("Number"),
                [new RangeConstraint(0d, 200d)])],
            [new AssignEffect(DomainExpression.Property("Total"), DomainExpression.Parameter("x"))],
            []);
        var a = new Poly.DomainModeling.Action("A", InvocationResult.Void,
            [new Property("amount", new DomainTypeReference("Number"),
                [new RangeConstraint(0d, 200d)])],
            [new InvokeActionEffect("B", [
                new PropertyBinding("x", DomainExpression.Parameter("amount"))
            ])],
            []);
        var entity = new Entity("Item", [qty, total], [a, b], [], []);
        var domain = DomainTestFactory.Create("Test", [entity], []);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var aInvariants = analysis.GetActionInvariants(a);
        await Assert.That(aInvariants).IsNotNull();
        var aPost = aInvariants!.StageContexts().Single().Postconditions.Single();
        await Assert.That(aPost.ValueRange!.Max).IsEqualTo(200d);
        var net = aPost.Constraints.OfType<RangeConstraint>().Single();
        await Assert.That(Convert.ToDouble(net.Maximum)).IsEqualTo(100d);
    }

    [Test]
    public async Task CallChain_PostconditionViolation_IsReportedOnCaller() {
        // A invokes `B(x: amount)` where amount range(0, 200). B's `assign Total to x`
        // (Total range(0, 50), x with no declared range) has no DIRECT postcondition
        // (x's range is unknown) — but under A's call-chain binding x ∈ [0, 200], the
        // postcondition can violate Total's range. The call-chain diagnostic must fire.
        var total = new Property("Total", new DomainTypeReference("Number"),
            [new RangeConstraint(0d, 50d)]);
        var b = new Poly.DomainModeling.Action("B", InvocationResult.Void,
            [new Property("x", new DomainTypeReference("Number"), [])],
            [new AssignEffect(DomainExpression.Property("Total"), DomainExpression.Parameter("x"))],
            []);
        var a = new Poly.DomainModeling.Action("A", InvocationResult.Void,
            [new Property("amount", new DomainTypeReference("Number"),
                [new RangeConstraint(0d, 200d)])],
            [new InvokeActionEffect("B", [
                new PropertyBinding("x", DomainExpression.Parameter("amount"))
            ])],
            []);
        var entity = new Entity("Item", [total], [a, b], [], []);
        var analysis = DomainModelAnalyzer.Analyze(DomainTestFactory.Create("Test", [entity], []));

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning
            && d.Message.Contains("Call-chain postcondition (A → B)")
            && d.Message.Contains("can fall outside constraint range(0, 50)"))).IsTrue();
    }

    [Test]
    public async Task ConstraintMerge_RangeAndLength_ProduceNetConstraint() {
        // Range: intersection. Length: intersection. Unsatisfiable: null.
        var a = new RangeConstraint(0d, 100d);
        var b = new RangeConstraint(20d, 80d);
        var net = a.Merge(b);
        await Assert.That(net).IsTypeOf<RangeConstraint>();
        var r = (RangeConstraint)net!;
        await Assert.That(Convert.ToDouble(r.Minimum)).IsEqualTo(20d);
        await Assert.That(Convert.ToDouble(r.Maximum)).IsEqualTo(80d);

        var len = new LengthConstraint(2, 10).Merge(new LengthConstraint(4, 6));
        await Assert.That(len).IsTypeOf<LengthConstraint>();
        var l = (LengthConstraint)len!;
        await Assert.That(l.MinLength).IsEqualTo(4);
        await Assert.That(l.MaxLength).IsEqualTo(6);

        var unsatisfiable = new RangeConstraint(0d, 5d).Merge(new RangeConstraint(10d, 20d));
        await Assert.That(unsatisfiable).IsNull();
    }

    [Test]
    public async Task Postcondition_ParamConstraintsMergedIntoNet() {
        // `assign Total to amount` where amount range(0,200) and Total range(0,100): the
        // postcondition's net constraint is the intersection range(0,100) — a parameter
        // provided to the action carries its own constraints into the effect.
        var total = new Property("Total", new DomainTypeReference("Number"),
            [new RangeConstraint(0d, 100d)]);
        var setTotal = new Poly.DomainModeling.Action("SetTotal", InvocationResult.Void,
            [new Property("amount", new DomainTypeReference("Number"),
                [new RangeConstraint(0d, 200d)])],
            [new AssignEffect(DomainExpression.Property("Total"),
                DomainExpression.Parameter("amount"))],
            []);
        var entity = new Entity("Item", [total], [setTotal], [], []);
        var domain = DomainTestFactory.Create("Test", [entity], []);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var invariants = analysis.GetActionInvariants(setTotal);
        await Assert.That(invariants).IsNotNull();
        var post = invariants!.StageContexts().Single().Postconditions.Single();
        var net = post.Constraints.OfType<RangeConstraint>().Single();
        await Assert.That(Convert.ToDouble(net.Minimum)).IsEqualTo(0d);
        await Assert.That(Convert.ToDouble(net.Maximum)).IsEqualTo(100d);
    }
}