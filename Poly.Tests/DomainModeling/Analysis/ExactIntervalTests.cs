using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// V-05b S1 unit rows for the exact interval core (B4, F5). Every op is pinned at the edge that the
/// double interval could not represent: an exact result that does not fit a decimal opens its endpoint
/// instead of rounding (OQ-6), <c>0 · ±∞</c> is 0, an open operand keeps the same-side endpoint open,
/// and a strict bound on an integral axis is exact while a decimal axis stays inclusive.
/// </summary>
public class ExactIntervalTests {
    private static ValueRange Range(decimal? min, decimal? max) => new(min, max);

    // ── Mul: an overflowing bound opens its side (B4 sign rule) ──────────

    [Test]
    public async Task Mul_OverflowingBound_BecomesOpenEndpoint() {
        var product = Range(0m, decimal.MaxValue).Mul(Range(0m, 2m));

        await Assert.That(product).IsNotNull();
        await Assert.That(product!.Min).IsEqualTo(0m);
        await Assert.That(product.Max).IsNull();
    }

    [Test]
    public async Task Mul_NegativeOverflowingBound_BecomesOpenEndpoint() {
        var product = Range(-decimal.MaxValue, 0m).Mul(Range(0m, 2m));

        await Assert.That(product).IsNotNull();
        await Assert.That(product!.Min).IsNull();
        await Assert.That(product.Max).IsEqualTo(0m);
    }

    /// <summary>T10b: an operand that is exactly <c>[0, 0]</c> makes the product 0 even when the other
    /// side is open — <c>0 · ±∞ = 0</c>, never unknown.</summary>
    [Test]
    public async Task Mul_OpenOperandByZero_IsZero() {
        var product = Range(0m, 0m).Mul(Range(null, 5m));

        await Assert.That(product).IsNotNull();
        await Assert.That(product!.Min).IsEqualTo(0m);
        await Assert.That(product.Max).IsEqualTo(0m);
    }

    [Test]
    public async Task Mul_UnknownOperand_IsUnknown() {
        await Assert.That(Range(0m, 10m).Mul(null)).IsNull();
    }

    // ── Add / Sub: an open operand or a null try-result opens that side ──

    [Test]
    public async Task Add_OverflowingBound_BecomesOpenEndpoint() {
        var sum = Range(0m, decimal.MaxValue).Add(Range(0m, 2m));

        await Assert.That(sum).IsNotNull();
        await Assert.That(sum!.Min).IsEqualTo(0m);
        await Assert.That(sum.Max).IsNull();
    }

    [Test]
    public async Task Add_NegativeOverflowingBound_BecomesOpenEndpoint() {
        var sum = Range(-decimal.MaxValue, 0m).Add(Range(-2m, 0m));

        await Assert.That(sum).IsNotNull();
        await Assert.That(sum!.Min).IsNull();
        await Assert.That(sum.Max).IsEqualTo(0m);
    }

    /// <summary>T12: an open operand endpoint opens that same side of the result; it is not "unknown".</summary>
    [Test]
    public async Task Add_OpenOperand_YieldsOpenEndpointNotUnknown() {
        var sum = Range(null, 10m).Add(Range(1m, 2m));

        await Assert.That(sum).IsNotNull();
        await Assert.That(sum!.Min).IsNull();
        await Assert.That(sum.Max).IsEqualTo(12m);
    }

    [Test]
    public async Task Sub_ClosedOperands_AreExact() {
        var difference = Range(0m, 10m).Sub(Range(2m, 3m));

        await Assert.That(difference).IsNotNull();
        await Assert.That(difference!.Min).IsEqualTo(-3m);
        await Assert.That(difference.Max).IsEqualTo(8m);
    }

    [Test]
    public async Task Sub_OpenOperand_OpensTheFlippedSide() {
        var difference = Range(null, 10m).Sub(Range(2m, 3m));

        await Assert.That(difference).IsNotNull();
        await Assert.That(difference!.Min).IsNull();
        await Assert.That(difference.Max).IsEqualTo(8m);
    }

    [Test]
    public async Task Add_UnknownOperand_IsUnknown() {
        await Assert.That(Range(0m, 10m).Add(null)).IsNull();
        await Assert.That(Range(0m, 10m).Sub(null)).IsNull();
    }

    // ── Below / Above: strict bounds (F5) ────────────────────────────────

    [Test]
    public async Task StrictLessThan_Number_IsExactMinusOne() {
        await Assert.That(Range(null, null).Below(100m, integral: true)!.Max).IsEqualTo(99m);
        await Assert.That(Range(null, null).Below(99.5m, integral: true)!.Max).IsEqualTo(99m);
    }

    [Test]
    public async Task StrictLessThan_Decimal_StaysInclusive() {
        await Assert.That(Range(null, null).Below(99.5m, integral: false)!.Max).IsEqualTo(99.5m);
    }

    [Test]
    public async Task StrictGreaterThan_Number_IsExactPlusOne() {
        await Assert.That(Range(null, null).Above(100m, integral: true)!.Min).IsEqualTo(101m);
        await Assert.That(Range(null, null).Above(99.5m, integral: true)!.Min).IsEqualTo(100m);
    }

    [Test]
    public async Task StrictGreaterThan_Decimal_StaysInclusive() {
        await Assert.That(Range(null, null).Above(99.5m, integral: false)!.Min).IsEqualTo(99.5m);
    }

    [Test]
    public async Task Below_KeepsTheTighterExistingEndpoint() {
        var narrowed = Range(0m, 50m).Below(100m, integral: true);

        await Assert.That(narrowed).IsNotNull();
        await Assert.That(narrowed!.Min).IsEqualTo(0m);
        await Assert.That(narrowed.Max).IsEqualTo(50m);
    }

    [Test]
    public async Task Above_KeepsTheTighterExistingEndpoint() {
        var narrowed = Range(50m, 200m).Above(10m, integral: true);

        await Assert.That(narrowed).IsNotNull();
        await Assert.That(narrowed!.Min).IsEqualTo(50m);
        await Assert.That(narrowed.Max).IsEqualTo(200m);
    }

    [Test]
    public async Task Below_EmptyRange_IsNoRange() {
        await Assert.That(Range(5m, 10m).Below(5m, integral: true)).IsNull();
    }

    [Test]
    public async Task Above_EmptyRange_IsNoRange() {
        await Assert.That(Range(5m, 10m).Above(10m, integral: true)).IsNull();
    }

    // ── Join / TryMeet ──────────────────────────────────────────────────

    [Test]
    public async Task Join_TakesTheHull() {
        var joined = Range(0m, 10m).Join(Range(5m, 20m));

        await Assert.That(joined).IsNotNull();
        await Assert.That(joined!.Min).IsEqualTo(0m);
        await Assert.That(joined.Max).IsEqualTo(20m);
    }

    [Test]
    public async Task Join_OpenEndpoint_StaysOpen() {
        var joined = Range(null, 10m).Join(Range(5m, 20m));

        await Assert.That(joined).IsNotNull();
        await Assert.That(joined!.Min).IsNull();
        await Assert.That(joined.Max).IsEqualTo(20m);
    }

    [Test]
    public async Task Join_UnknownOperand_IsUnknown() {
        await Assert.That(Range(0m, 10m).Join(null)).IsNull();
    }

    [Test]
    public async Task TryMeet_OverlappingRanges_IsTheIntersection() {
        var met = Range(0m, 10m).TryMeet(Range(5m, 20m), out var meet);

        await Assert.That(met).IsTrue();
        await Assert.That(meet.Min).IsEqualTo(5m);
        await Assert.That(meet.Max).IsEqualTo(10m);
    }

    [Test]
    public async Task TryMeet_DisjointRanges_IsEmpty() {
        await Assert.That(Range(0m, 10m).TryMeet(Range(11m, 20m), out _)).IsFalse();
    }

    [Test]
    public async Task TryMeet_UnknownOperand_IsEmpty() {
        await Assert.That(Range(0m, 10m).TryMeet(null, out _)).IsFalse();
    }

    // ── The range itself ────────────────────────────────────────────────

    [Test]
    public async Task ToRangeConstraint_BothEndpointsOpen_IsNoConstraint() {
        await Assert.That(Range(null, null).ToRangeConstraint()).IsNull();
        await Assert.That(Range(0m, 10m).ToRangeConstraint()!.Minimum).IsEqualTo(0m);
    }

    // ── V-05b S2: the coupled invariants of the exact evaluation path ─────
    // One shared evaluator: the literal arm, Refine, NumericRange, the transfer table, X − X and the
    // ToRangeConstraint round-trip. Each invariant is pinned by a test named in the S2 commit message.

    private static EvolutionResult Parse(string dsl) {
        var changes = new PolyDslParser(dsl).Parse();
        return new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
    }

    private static List<Poly.Analysis.Diagnostic> EffectErrors(EvolutionResult result) =>
        result.Analysis.Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation
                && d.Severity == DiagnosticSeverity.Error)
            .ToList();

    private static Entity NumberEntity() =>
        new("Item", [new Property("U", new DomainTypeReference("Number"), [])], [], [], []);

    private static (decimal? Min, decimal? Max) Infer(DomainExpression expression) =>
        EffectInvariantAnalyzer.InferNumericRange(expression, NumberEntity(), action: null);

    /// <summary>Invariant 3 (T1): <see cref="AbstractValue.NumericRange"/> keeps a bound above 2^53.</summary>
    [Test]
    public async Task ValueRange_BoundsAbove2Pow53_AreExact() {
        var range = AbstractValue.From([new RangeConstraint(9007199254740993L, null)]).NumericRange;

        await Assert.That(range!.Min).IsEqualTo(9007199254740993m);
        await Assert.That(range.Max).IsNull();
    }

    /// <summary>Invariant 1 (T2): the literal arm keeps its declared bound; it is not rounded through double.</summary>
    [Test]
    public async Task Assign_LiteralAbove2Pow53_PlusZero_IsError() {
        var result = Parse("""
            domain Test
            A: entity {
              N: Number range(0, 9007199254740992)
              Dep: action { assign N to 9007199254740993 + 0 }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(EffectErrors(result).Count).IsEqualTo(1);
    }

    /// <summary>Invariant 4 (T5): the transfer table decides 0 · unknown = 0.</summary>
    [Test]
    public async Task Mul_UnboundedOperandByZero_IsZero() {
        var product = Infer(DomainExpression.Multiply(DomainExpression.Property("U"), DomainExpression.Literal(0L)));

        await Assert.That(product).IsEqualTo(((decimal?)0m, (decimal?)0m));
    }

    /// <summary>Invariant 4 (T5 E2E): <c>U * 0</c> is 0 and outside <c>range(5, 20)</c>.</summary>
    [Test]
    public async Task Assign_UnboundedPropertyTimesZero_IsError() {
        var result = Parse("""
            domain Test
            A: entity {
              U: Number
              B: Number range(5, 20)
              Dep: action { assign B to U * 0 }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(EffectErrors(result).Count).IsEqualTo(1);
    }

    /// <summary>Invariant 5 (T6): <c>U − U</c> cancels to 0 before the (absent) range is consulted.</summary>
    [Test]
    public async Task Sub_SamePropertyOperands_IsZero() {
        var result = Parse("""
            domain Test
            A: entity {
              U: Number
              B: Number range(5, 20)
              Dep: action { assign B to U - U }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(EffectErrors(result).Count).IsEqualTo(1);
    }

    /// <summary>Invariant 5: structurally equal pure subtrees cancel, ignoring node ids.</summary>
    [Test]
    public async Task Sub_NestedEqualSubtrees_IsZero() {
        var result = Parse("""
            domain Test
            A: entity {
              U: Number
              B: Number range(5, 20)
              Dep: action { assign B to (U + 1) - (U + 1) }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(EffectErrors(result).Count).IsEqualTo(1);
        await Assert.That(Infer(DomainExpression.Subtract(
            DomainExpression.Add(DomainExpression.Property("U"), DomainExpression.Literal(1L)),
            DomainExpression.Add(DomainExpression.Property("U"), DomainExpression.Literal(1L)))))
            .IsEqualTo(((decimal?)0m, (decimal?)0m));
    }

    /// <summary>Invariant 5: an operand with an unmodelled operator is not known to be pure, so
    /// <c>f() − f()</c> does not cancel (the shared purity gate, B7).</summary>
    [Test]
    public async Task Sub_ImpureOperands_DoNotCancel() {
        var call = DomainExpression.Call("f", []);

        await Assert.That(Infer(DomainExpression.Subtract(call, DomainExpression.Call("f", []))))
            .IsEqualTo(((decimal?)null, (decimal?)null));
        await Assert.That(Infer(DomainExpression.Subtract(
            DomainExpression.Add(call, DomainExpression.Literal(1L)),
            DomainExpression.Add(DomainExpression.Call("f", []), DomainExpression.Literal(1L)))))
            .IsEqualTo(((decimal?)null, (decimal?)null));
    }

    private static async Task<ValueRange?> NarrowedQty(DomainExpression condition, RangeConstraint declared) {
        var qty = new Property("Qty", new DomainTypeReference("Decimal"), [declared]);
        var act = new Poly.DomainModeling.Ontology.Action("Touch", InvocationResult.Void, [], [], [new Policy("Guard", condition)]);
        var entity = new Entity("Item", [qty], [act], [], []);
        var analysis = DomainModelAnalyzer.Analyze(DomainTestFactory.Create("Test", [entity], []));
        var context = analysis.GetActionInvariants(act)!.StageContexts().Single();
        return await Task.FromResult(context.NarrowedRanges["Qty"]);
    }

    /// <summary>Invariant 2 (T10): a strict bound on a Decimal axis is the inclusive bound, exactly, and
    /// the declared minimum survives (the range axis is not dropped).</summary>
    [Test]
    public async Task StrictLessThan_Decimal_RefinesToInclusiveBound_Sound() {
        var narrowed = await NarrowedQty(
            DomainExpression.LessThan(DomainExpression.Property("Qty"), DomainExpression.Literal(5.5m)),
            new RangeConstraint(0m, 100m));

        await Assert.That(narrowed!.Min).IsEqualTo(0m);
        await Assert.That(narrowed.Max).IsEqualTo(5.5m);
    }

    /// <summary>Invariant 2: a refinement bound above 2^53 is not rounded through double.</summary>
    [Test]
    public async Task RefineNarrowedValue_KeepsDeclaredRangeAxis() {
        var narrowed = await NarrowedQty(
            DomainExpression.LessThanOrEqual(DomainExpression.Property("Qty"), DomainExpression.Literal(9007199254740993L)),
            new RangeConstraint(0L, 9007199254740994L));

        await Assert.That(narrowed!.Min).IsEqualTo(0m);
        await Assert.That(narrowed.Max).IsEqualTo(9007199254740993m);
    }

    /// <summary>Invariant 6 (A1): the round trip keeps CLR decimal bounds; an ExactNumber is never boxed.</summary>
    [Test]
    public async Task ToRangeConstraint_RoundTripsAsDecimalBounds() {
        var range = Range(5.5m, 9007199254740993m);
        var constraint = range.ToRangeConstraint()!;

        await Assert.That(constraint.Minimum!.GetType()).IsEqualTo(typeof(decimal));
        await Assert.That(constraint.Maximum!.GetType()).IsEqualTo(typeof(decimal));
        await Assert.That(AbstractValue.From([constraint]).NumericRange).IsEqualTo(range);
    }

    // ── V-05b S3: A13 boundary pins for the endpoint type ────────────────

    /// <summary>A13: a bool literal carries no numeric range — it is not <c>1</c>/<c>0</c>.</summary>
    [Test]
    public async Task BoolLiteral_HasNoRange() {
        await Assert.That(Infer(DomainExpression.Literal(true))).IsEqualTo(((decimal?)null, (decimal?)null));
        await Assert.That(Infer(DomainExpression.Literal(false))).IsEqualTo(((decimal?)null, (decimal?)null));
    }

    /// <summary>A13: an API-built fractional <c>double</c> bound has no exact endpoint, so the value
    /// carries no interval at all. Ordering still falls back to master's double comparison (V-05a FX4),
    /// so this is an interval fact, not a new violated verdict.</summary>
    [Test]
    public async Task FractionalDoubleBound_IsUnorderable() {
        await Assert.That(AbstractValue.From([new RangeConstraint(0.5d, null)]).NumericRange).IsNull();
    }

    // ── V-05b S4: provable truth and the transfer registry (A4) ───────────

    private static Dictionary<string, AbstractValue> TruthEnv() => new(StringComparer.Ordinal) {
        ["X"] = AbstractValue.From([new RangeConstraint(0m, 5m)]),
        ["Y"] = AbstractValue.From([new RangeConstraint(10m, 20m)]),
        ["Z"] = AbstractValue.From([new RangeConstraint(3m, 9m)]),
    };

    /// <summary>T11a: a comparison over intervals is true when every pair satisfies it, false when no
    /// pair does, and unknown otherwise — literals and transfer-built intervals included.</summary>
    [Test]
    public async Task ProvableTruth_ComparisonOverIntervals_IsTrueFalseUnknown() {
        var env = TruthEnv();

        await Assert.That(ProvableTruth.Decide(
            DomainExpression.LessThan(DomainExpression.Property("X"), DomainExpression.Property("Y")), env))
            .IsEqualTo(Truth.True);
        await Assert.That(ProvableTruth.Decide(
            DomainExpression.GreaterThan(DomainExpression.Property("X"), DomainExpression.Property("Y")), env))
            .IsEqualTo(Truth.False);
        await Assert.That(ProvableTruth.Decide(
            DomainExpression.LessThan(DomainExpression.Property("X"), DomainExpression.Property("Z")), env))
            .IsEqualTo(Truth.Unknown);

        // A literal on one side, and an interval the transfer table built (X + 1 is [1, 6]).
        await Assert.That(ProvableTruth.Decide(
            DomainExpression.GreaterThanOrEqual(DomainExpression.Property("Y"), DomainExpression.Literal(10m)), env))
            .IsEqualTo(Truth.True);
        await Assert.That(ProvableTruth.Decide(
            DomainExpression.LessThan(DomainExpression.Add(DomainExpression.Property("X"), DomainExpression.Literal(1m)),
                DomainExpression.Property("Y")), env))
            .IsEqualTo(Truth.True);

        // An unmodelled leaf is never a guess.
        await Assert.That(ProvableTruth.Decide(DomainExpression.Call("f", []), env)).IsEqualTo(Truth.Unknown);
    }

    /// <summary>T11b: And/Or are Kleene three-valued and Not flips only a decided answer.</summary>
    [Test]
    public async Task ProvableTruth_AndOrNot_ThreeValued() {
        var env = TruthEnv();
        var yes = DomainExpression.LessThan(DomainExpression.Property("X"), DomainExpression.Property("Y"));
        var no = DomainExpression.GreaterThan(DomainExpression.Property("X"), DomainExpression.Property("Y"));
        var maybe = DomainExpression.LessThan(DomainExpression.Property("X"), DomainExpression.Property("Z"));

        await Assert.That(ProvableTruth.Decide(DomainExpression.And(yes, no), env)).IsEqualTo(Truth.False);
        await Assert.That(ProvableTruth.Decide(DomainExpression.And(no, maybe), env)).IsEqualTo(Truth.False);
        await Assert.That(ProvableTruth.Decide(DomainExpression.And(yes, maybe), env)).IsEqualTo(Truth.Unknown);
        await Assert.That(ProvableTruth.Decide(DomainExpression.And(yes, yes), env)).IsEqualTo(Truth.True);

        await Assert.That(ProvableTruth.Decide(DomainExpression.Or(no, maybe), env)).IsEqualTo(Truth.Unknown);
        await Assert.That(ProvableTruth.Decide(DomainExpression.Or(yes, maybe), env)).IsEqualTo(Truth.True);
        await Assert.That(ProvableTruth.Decide(DomainExpression.Or(no, no), env)).IsEqualTo(Truth.False);

        await Assert.That(ProvableTruth.Decide(DomainExpression.Not(yes), env)).IsEqualTo(Truth.False);
        await Assert.That(ProvableTruth.Decide(DomainExpression.Not(no), env)).IsEqualTo(Truth.True);
        await Assert.That(ProvableTruth.Decide(DomainExpression.Not(maybe), env)).IsEqualTo(Truth.Unknown);
    }

    /// <summary>T12: a node with no transfer row is unknown, and a registered row still transfers.</summary>
    [Test]
    public async Task IntervalTransfers_UnregisteredNode_IsUnknown() {
        var divide = DomainExpression.Divide(DomainExpression.Literal(4m), DomainExpression.Literal(2m));
        await Assert.That(IntervalTransfers.Transfer(divide, [Range(4m, 4m), Range(2m, 2m)])).IsNull();

        var add = DomainExpression.Add(DomainExpression.Literal(1m), DomainExpression.Literal(2m));
        await Assert.That(IntervalTransfers.Transfer(add, [Range(1m, 1m), Range(2m, 2m)])!.Min).IsEqualTo(3m);
    }

    // ── V-05b fix round 1 ─────────────────────────────────────────────────

    /// <summary>T1: a call-chain postcondition with no value range is skipped silently; the callee's own
    /// assign already records DMCS003 for the write, so the chain adds neither a row nor a warning.</summary>
    [Test]
    public async Task CallChain_PostconditionWithNoValueRange_IsSkippedSilently() {
        var result = Parse("""
            domain Test
            A: entity {
              U: Number
              D: Number range(0, 100)
              Set: action { assign D to U + 1 }
              Go: action { invoke Set }
            }
            """);
        var chain = result.Analysis.Diagnostics
            .Where(d => d.Message.Contains("Call-chain postcondition (Go → Set)", StringComparison.Ordinal))
            .ToList();

        await Assert.That(chain).IsEmpty();
        await Assert.That(result.Analysis.GetVerification(result.Root).UnverifiedWrites.Count).IsEqualTo(1);
    }

    /// <summary>T3: a callee write reachable from two stage contexts of the caller is one finding, not two
    /// (V-03 A2, one finding per effect), matching the one fact per effect.</summary>
    [Test]
    public async Task CallChain_CalleeWriteInTwoStageContexts_IsOneDiagnostic() {
        var qty = new Property("Qty", new DomainTypeReference("Number"), [new RangeConstraint(0L, 100L)]);
        var d = new Property("D", new DomainTypeReference("Number"), [new RangeConstraint(0L, 100L)]);
        var set = new Poly.DomainModeling.Ontology.Action("Set", InvocationResult.Void, [], [
            new AssignEffect(DomainExpression.Property("D"),
                DomainExpression.Add(DomainExpression.Property("Qty"), DomainExpression.Literal(10L)))
        ], []);
        var go = new Poly.DomainModeling.Ontology.Action("Go", InvocationResult.Void, [],
            [new InvokeActionEffect("Set", [])], []);
        var a = new Stage("A", Actions: [], Policies: [
            new Policy("A95", DomainExpression.LessThanOrEqual(DomainExpression.Property("Qty"), DomainExpression.Literal(95L)))
        ], OnEntryEffects: [], OnExitEffects: []);
        var b = new Stage("B", Actions: [], Policies: [
            new Policy("B98", DomainExpression.LessThanOrEqual(DomainExpression.Property("Qty"), DomainExpression.Literal(98L)))
        ], OnEntryEffects: [], OnExitEffects: []);
        var entity = new Entity("Item", [qty, d], [set, go], [], [a, b]);
        var domain = DomainTestFactory.Create("Test", [entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.GetActionInvariants(go)!.StageContexts().Count).IsEqualTo(2);
        var chain = analysis.Diagnostics
            .Where(x => x.Message.Contains("Call-chain postcondition (Go → Set)", StringComparison.Ordinal))
            .ToList();
        await Assert.That(chain.Count).IsEqualTo(1);
    }

    /// <summary>T5: <c>line Qty − line Qty</c> cancels through the for-each binding evaluator too (A10).</summary>
    [Test]
    public async Task ForEachBinding_SameBinderPropertyDifference_IsZero() {
        var result = Parse("""
            domain Test
            Line: entity {
              Qty: Number
              T: Number range(5, 20)
              Mark: action (amount: Number) { assign T to amount }
            }
            Order: entity {
              lines: many Line
              Go: action { for lines as line invoke line.Mark(amount: line Qty - line Qty) }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(EffectErrors(result).Count).IsGreaterThanOrEqualTo(1);
    }

    /// <summary>T5: the Mul mirror of <c>[0,0] · open</c>: <c>open · [0,0]</c> is [0,0].</summary>
    [Test]
    public async Task Mul_OpenByZeroOperand_IsZero() {
        var product = Range(null, 5m).Mul(Range(0m, 0m));

        await Assert.That(product).IsNotNull();
        await Assert.That(product!.Min).IsEqualTo(0m);
        await Assert.That(product.Max).IsEqualTo(0m);
    }

    /// <summary>T5: a non-zero operand times an open operand stays open (not 0, not unknown-as-bounded).</summary>
    [Test]
    public async Task Mul_NonZeroOperandByOpen_StaysOpen() {
        var product = Range(1m, 2m).Mul(Range(null, null));

        await Assert.That(product is null || (product.Min is null && product.Max is null)).IsTrue();
    }

    // ── V-05b fix round 2: the worst verdict across stage contexts wins ───

    private static Domain TwoStageChainDomain(bool violatedStageFirst) {
        var qty = new Property("Qty", new DomainTypeReference("Number"), [new RangeConstraint(0L, 100L)]);
        var d = new Property("D", new DomainTypeReference("Number"), [new RangeConstraint(0L, 100L)]);
        var set = new Poly.DomainModeling.Ontology.Action("Set", InvocationResult.Void,
            [new Property("amount", new DomainTypeReference("Number"), [])],
            [new AssignEffect(DomainExpression.Property("D"), DomainExpression.Parameter("amount"))], []);
        var go = new Poly.DomainModeling.Ontology.Action("Go", InvocationResult.Void, [],
            [new InvokeActionEffect("Set", [new PropertyBinding("amount",
                DomainExpression.Add(DomainExpression.Property("Qty"), DomainExpression.Literal(10L)))])], []);
        var undecided = new Stage("A", Actions: [], Policies: [
            new Policy("Low", DomainExpression.LessThanOrEqual(DomainExpression.Property("Qty"), DomainExpression.Literal(95L)))
        ], OnEntryEffects: [], OnExitEffects: []);
        var violated = new Stage("B", Actions: [], Policies: [
            new Policy("High", DomainExpression.GreaterThanOrEqual(DomainExpression.Property("Qty"), DomainExpression.Literal(91L)))
        ], OnEntryEffects: [], OnExitEffects: []);
        Stage[] stages = violatedStageFirst ? [violated, undecided] : [undecided, violated];
        return DomainTestFactory.Create("Test", [new Entity("Item", [qty, d], [set, go], [], stages)], []);
    }

    private static async Task AssertViolatedWins(Domain domain) {
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var errors = analysis.Diagnostics.Where(x =>
            x.Code == DomainModelDiagnosticCodes.EffectConstraintViolation
            && x.Severity == DiagnosticSeverity.Error
            && x.Message.Contains("Call-chain postcondition (Go → Set)", StringComparison.Ordinal)
            && x.Message.Contains("entirely outside", StringComparison.Ordinal)).ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(analysis.GetVerification(domain).Verdict).IsEqualTo(CheckVerdict.Violated);
    }

    [Test]
    public async Task CallChain_ViolatedStageAfterUndecidedStage_IsReportedAsError() =>
        await AssertViolatedWins(TwoStageChainDomain(violatedStageFirst: false));

    [Test]
    public async Task CallChain_ViolatedStageBeforeUndecidedStage_IsReportedAsError() =>
        await AssertViolatedWins(TwoStageChainDomain(violatedStageFirst: true));
}