using Poly.DomainModeling.Analysis;
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
}
