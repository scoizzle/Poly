using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// V-03 (Q4) unit A: <see cref="WriteVerification.DecideRange"/> is the one range decision
/// procedure. It reads absent endpoints explicitly — a null side on the target is satisfied
/// (there is no bound to break), a null side on the value against a closed target is Undecided
/// (never Violated) — and it never reports Satisfied without a value range.
/// </summary>
public class WriteVerificationTests {
    private static AbstractValue Value(double? min, double? max) =>
        AbstractValue.From([new RangeConstraint(min, max)]);

    [Test]
    public async Task RangeDecide_Disjoint_IsViolated() {
        var verdict = WriteVerification.DecideRange(new RangeConstraint(0, 5), Value(10, 20));
        await Assert.That(verdict).IsEqualTo(ConstraintVerdict.Violated);
    }

    [Test]
    public async Task RangeDecide_Overlapping_IsUndecided() {
        var verdict = WriteVerification.DecideRange(new RangeConstraint(5, 20), Value(0, 10));
        await Assert.That(verdict).IsEqualTo(ConstraintVerdict.Undecided);
    }

    [Test]
    public async Task RangeDecide_Contained_IsSatisfied() {
        var verdict = WriteVerification.DecideRange(new RangeConstraint(5, 20), Value(6, 9));
        await Assert.That(verdict).IsEqualTo(ConstraintVerdict.Satisfied);
    }

    [Test]
    public async Task RangeDecide_NoValueRange_IsUndecided() {
        var verdict = WriteVerification.DecideRange(new RangeConstraint(0, 5), AbstractValue.Unknown);
        await Assert.That(verdict).IsEqualTo(ConstraintVerdict.Undecided);
    }

    [Test]
    public async Task OpenTarget_IsSatisfied() {
        // range(0, ) places no upper bound, so a value above the lower bound is fully contained.
        var verdict = WriteVerification.DecideRange(new RangeConstraint(0, null), Value(1, 5));
        await Assert.That(verdict).IsEqualTo(ConstraintVerdict.Satisfied);
    }

    [Test]
    public async Task OpenValueEnd_VsClosedTarget_IsUndecided() {
        // [0, +∞) can exceed a closed target: not contained, but not disjoint either.
        var verdict = WriteVerification.DecideRange(new RangeConstraint(0, 100), Value(0, null));
        await Assert.That(verdict).IsEqualTo(ConstraintVerdict.Undecided);
    }

    [Test]
    public async Task OpenValueStart_VsClosedTarget_IsUndecided() {
        // (-∞, 100] can fall below a closed target: Undecided, never Violated.
        var verdict = WriteVerification.DecideRange(new RangeConstraint(0, 100), Value(null, 100));
        await Assert.That(verdict).IsEqualTo(ConstraintVerdict.Undecided);
    }

    [Test]
    public async Task Facts_AreKeyedOnDomain() {
        // V-04 reads one facts bucket per domain; two domains in one run must not share it.
        var context = AnalysisContext.CreateDefault();
        var first = DomainTestFactory.Create("First", [], []);
        var second = DomainTestFactory.Create("Second", [], []);

        var facts = WriteVerificationFactsStore.For(context, first);

        await Assert.That(WriteVerificationFactsStore.For(context, first)).IsSameReferenceAs(facts);
        await Assert.That(WriteVerificationFactsStore.For(context, second)).IsNotSameReferenceAs(facts);
    }
}