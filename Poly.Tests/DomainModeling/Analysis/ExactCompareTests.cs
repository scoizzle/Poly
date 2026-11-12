using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// V-05a (#680): every analysis-time comparison orders its operands exactly. Above 2^53 a
/// <c>double</c> cannot hold a <c>long</c>, so a comparison routed through <c>double</c> silently
/// equalises neighbours and turns a true guard into "always false" (#643).
/// </summary>
public class ExactCompareTests {
    private const long Above2Pow53 = 9007199254740993L;
    private const long Above2Pow53Minus1 = 9007199254740992L;

    [Test]
    public async Task TryCompare_LongVsDecimal_IsExact() {
        var order = ConstraintMerge.TryCompare(Above2Pow53, 9007199254740992m);

        await Assert.That(order).IsNotNull();
        await Assert.That(order!.Value).IsGreaterThan(0);
    }

    [Test]
    public async Task CycleGuard_LiteralAbove2Pow53_IsNotProvablyFalse() {
        // 9007199254740993 > 9007199254740992 is true; double reads both as ...992.0.
        var comparison = new Comparison(
            DomainExpression.Literal(Above2Pow53),
            DomainExpression.Literal(Above2Pow53Minus1),
            ComparisonKind.GreaterThan);

        await Assert.That(EffectAnalyzer.IsProvablyAlwaysTrue(comparison)).IsTrue();
        await Assert.That(EffectAnalyzer.IsProvablyAlwaysFalse(comparison)).IsFalse();
    }

    [Test]
    public async Task ContractBoundaryCheck_VerdictAbove2Pow53_IsExact() {
        // range(0, ...993) against the bound ...992: the maximum is above the bound, so a
        // LessThanOrEqual comparison is not proven and not violated — it is unknown.
        Constraint[] constraints = [new RangeConstraint(0L, Above2Pow53)];

        var verdict = ContractBoundaryCheck.ComparisonVerdict(
            constraints, ComparisonKind.LessThanOrEqual, Above2Pow53Minus1);

        await Assert.That(verdict).IsEqualTo(ContractBoundaryVerdict.Unknown);
    }

    [Test]
    public async Task NumberEquals_Above2Pow53_IsExact() {
        // An equality to ...992 against the literal ...993 is a violation, never a proof.
        Constraint[] constraints = [new EqualityConstraint(Above2Pow53Minus1)];

        var verdict = ContractBoundaryCheck.ConstantVerdict(constraints[0], Above2Pow53);

        await Assert.That(verdict).IsEqualTo(ContractBoundaryVerdict.Violated);
    }

    [Test]
    public async Task AdjustRange_BoundAbove2Pow53_IsExact() {
        var adjusted = ConstraintPropagationAnalyzer.AdjustRangeRange(
            new RangeConstraint(Above2Pow53, null), 1);

        await Assert.That(adjusted).IsNotNull();
        await Assert.That(adjusted!.Minimum).IsTypeOf<decimal>();
        await Assert.That(adjusted.Minimum).IsEqualTo(9007199254740992m);
    }

    // ── F1: the exact compare must never be worse than master ───────────
    //
    // ValueRange is still double in V-05a, so Refine/Eval/ComposeArithmetic hand TryCompare a
    // fractional double. A null order there drops the whole range axis or downgrades an error,
    // so TryCompare falls back to master's double comparison for a double/float operand.

    [Test]
    public async Task Narrow_FractionalDoubleBoundPastDecimalRange_IsUnsatisfiable_F1() {
        var declared = AbstractValue.From([new RangeConstraint(0m, 100m)]);

        var narrowed = declared.Narrow(new RangeConstraint(150.5d, null));

        await Assert.That(narrowed.Unsatisfiable).IsTrue();
    }

    [Test]
    public async Task Narrow_FractionalDoubleBoundInsideDecimalRange_KeepsTheRange_F1() {
        var narrowed = AbstractValue.From([new RangeConstraint(0m, 100m)])
            .Narrow(new RangeConstraint(0.5d, null));

        await Assert.That(narrowed.Range).IsNotNull();
    }

    [Test]
    public async Task DecideRange_FractionalDoubleValueBelowDecimalTarget_IsViolated_F1() {
        var value = AbstractValue.From([new RangeConstraint(0.01d, 0.1d)]);

        var verdict = WriteVerification.DecideRange(new RangeConstraint(1m, 10m), value);

        await Assert.That(verdict).IsEqualTo(ConstraintVerdict.Violated);
    }

    [Test]
    public async Task Guard_FractionalBoundAboveDecimalRange_IsUnsatisfiablePrecondition_F1() {
        // DSL reachable: a Decimal property with a fractional guard literal. Master reported the
        // unsatisfiable precondition; a null order silently dropped the range axis instead.
        var result = Analyze("""
            domain Test
            Item: entity {
              Price: Decimal range(0, 100)
              Big: policy { Price > 150.5 }
              Run: action
                require Big
              {
                assign Price to 1
              }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("unsatisfiable preconditions"))).IsTrue();
    }

    // ── F2: an ordering analysis cannot make is not a violated claim ─────

    [Test]
    public async Task ConstantVerdict_EqualNumbersThatCannotBeOrdered_IsNotViolated_F2() {
        var verdict = ContractBoundaryCheck.ConstantVerdict(
            new EqualityConstraint(ulong.MaxValue), ulong.MaxValue);

        await Assert.That(verdict).IsNotEqualTo(ContractBoundaryVerdict.Violated);
    }

    [Test]
    public async Task Evaluate_FractionalDoubleInsideRange_IsNotViolated_F2() {
        var verdict = ConstraintValidation.Evaluate(new RangeConstraint(1L, 10L), 5.5d);

        await Assert.That(verdict).IsNotEqualTo(ConstraintVerdict.Violated);
    }

    // ── S3: Equal/NotEqual literal guards are numeric too ────────────────

    [Test]
    public async Task IsProvablyAlwaysFalse_EqualMixedNumericTypes_IsNotProven_S3() {
        var comparison = new Comparison(
            DomainExpression.Literal(1L), DomainExpression.Literal(1m), ComparisonKind.Equal);

        await Assert.That(EffectAnalyzer.IsProvablyAlwaysFalse(comparison)).IsFalse();
    }

    [Test]
    public async Task CycleGuard_EqualMixedAbove2Pow53_IsProvablyTrue() {
        var comparison = new Comparison(
            DomainExpression.Literal(Above2Pow53),
            DomainExpression.Literal(9007199254740993m),
            ComparisonKind.Equal);

        await Assert.That(EffectAnalyzer.IsProvablyAlwaysTrue(comparison)).IsTrue();
        await Assert.That(EffectAnalyzer.IsProvablyAlwaysFalse(comparison)).IsFalse();
    }

    [Test]
    public async Task EntryGuard_TrueComparisonAbove2Pow53_IsAnUnconditionalCycle() {
        // The guard is provably true, so both entry edges are unconditional and DMEFF013 applies.
        var result = Analyze("""
            domain E5
            Z: entity {
              Tag: Text
              A: stage { entry { if (9007199254740993 > 9007199254740992) { transition to B } } }
              B: stage { entry { transition to A } }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.UnconditionalAutomaticTransitionCycle)).IsTrue();
    }

    // ── FX1: the ordering arms must keep master's double fallback ────────
    //
    // ExactNumber.Compare rejects every fractional double, so ordering arms on ExactNumber alone
    // are worse than master for an API-built literal pair. ConstraintMerge.TryCompare is the one
    // ordering rule: exact first, master's double comparison when the exact order is missing.

    [Test]
    public async Task IsProvablyAlwaysTrue_FractionalDoubleOrdering_IsTrue_FX1() {
        var comparison = new Comparison(
            DomainExpression.Literal(0.5d), DomainExpression.Literal(0.25d), ComparisonKind.GreaterThan);

        await Assert.That(EffectAnalyzer.IsProvablyAlwaysTrue(comparison)).IsTrue();
        await Assert.That(EffectAnalyzer.IsProvablyAlwaysFalse(comparison)).IsFalse();
    }

    [Test]
    public async Task EntryGuard_FractionalDoubleComparison_IsAnUnconditionalCycle_FX1() {
        var result = Analyze("""
            domain E6
            Z: entity {
              Tag: Text
              A: stage { entry { if (0.5 > 0.25) { transition to B } } }
              B: stage { entry { transition to A } }
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.UnconditionalAutomaticTransitionCycle)).IsTrue();
    }

    // ── FX2: two numeric types that are not exactly representable keep master's order ──

    [Test]
    public async Task TryCompare_UlongAboveLongMax_KeepsMasterOrder_FX2() {
        var order = ConstraintMerge.TryCompare(ulong.MaxValue, 1L);

        await Assert.That(order).IsNotNull();
        await Assert.That(order!.Value).IsGreaterThan(0);
    }

    // ── FX3: the order path is for numbers; everything else stays object.Equals ──

    [Test]
    public async Task IsProvablyAlwaysTrue_SoftHyphenStringEquality_IsNotProven_FX3() {
        // Ordinal equality, as on master: CompareTo is culture-sensitive and ignores the soft hyphen.
        var comparison = new Comparison(
            DomainExpression.Literal("a\u00ADb"), DomainExpression.Literal("ab"), ComparisonKind.Equal);

        await Assert.That(EffectAnalyzer.IsProvablyAlwaysTrue(comparison)).IsFalse();
        await Assert.That(EffectAnalyzer.IsProvablyAlwaysFalse(comparison)).IsTrue();
    }

    // ── FX4: a numeric bound is orderable through the fallback ───────────

    [Test]
    public async Task HasUnorderableBound_FractionalDoubleBound_IsOrderable_FX4() {
        await Assert.That(WriteVerification.HasUnorderableBound(new RangeConstraint(0.5d, null))).IsFalse();
        await Assert.That(WriteVerification.HasUnorderableBound(new RangeConstraint(null, 0.5d))).IsFalse();
        // A non-numeric bound stays unorderable.
        await Assert.That(WriteVerification.HasUnorderableBound(new RangeConstraint("x", null))).IsTrue();
    }

    // ── FX5: the type-family split is pinned ─────────────────────────────

    [Test]
    public async Task Evaluate_TextAgainstNumberRange_IsViolated_FX5() {
        await Assert.That(ConstraintValidation.Evaluate(new RangeConstraint(1L, 10L), "x"))
            .IsEqualTo(ConstraintVerdict.Violated);
    }

    private static EvolutionResult Analyze(string dsl) {
        var changes = new PolyDslParser(dsl).Parse();
        return new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
    }
}