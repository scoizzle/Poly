using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #367: the lattice JOIN (least upper bound) beside the existing meet. A fact survives a join
/// only if it is true on BOTH arms; the meet keeps narrowings and calls disjoint ranges Empty.
/// </summary>
public class ConstraintJoinTests {
    private sealed record UnregisteredConstraint : Constraint;

    private static bool Same(Constraint? actual, Constraint expected) =>
        actual is not null && ConstraintMerge.Identical(actual, expected);

    private static RangeConstraint R(object? min, object? max) => new(min, max);

    // ---- Range ----

    [Test]
    public async Task Join_DisjointRanges_IsTheHullNotEmpty() {
        // #367 A-4 (authorised refinement): the join is the UNION {[0,10],[20,30]}; the hull is
        // still on the record and the union is not identical to the hull.
        var joined = (RangeConstraint)R(0, 10).Join(R(20, 30))!;
        await Assert.That(joined.Minimum).IsEqualTo(0);
        await Assert.That(joined.Maximum).IsEqualTo(30);
        await Assert.That(Same(joined, R(0, 30))).IsFalse();
        await Assert.That(joined.Intervals!.Count).IsEqualTo(2);
        await Assert.That(ConstraintValidation.Evaluate(joined, 15)).IsEqualTo(ConstraintVerdict.Violated);
        await Assert.That(ConstraintValidation.Evaluate(joined, 5)).IsEqualTo(ConstraintVerdict.Satisfied);
        await Assert.That(ConstraintValidation.Evaluate(joined, 25)).IsEqualTo(ConstraintVerdict.Satisfied);
    }

    [Test]
    public async Task Join_RangeBoundOnOneArmOnly_IsUnbounded() {
        // x >= 5 on one arm, nothing on the other: the lower bound must NOT survive.
        var joined = R(5, 10).Join(R(null, 8));
        await Assert.That(Same(joined, R(null, 10))).IsTrue();
    }

    [Test]
    public async Task Join_RangeWithNoCommonBound_DropsTheAxis() {
        await Assert.That(R(5, null).Join(R(null, 8))).IsNull();
    }

    [Test]
    public async Task Join_MixedTypeBounds_DropsTheAxisWithoutThrowing() {
        await Assert.That(R("a", "z").Join(R(1, 5))).IsNull();
        await Assert.That(R(DateTime.MinValue, null).Join(R(1, null))).IsNull();
    }

    [Test]
    public async Task Join_DecimalBounds_AreExactNotRoundedThroughDouble() {
        var a = 9007199254740993m;
        var b = 9007199254740992m;
        var joined = (RangeConstraint)R(a, a).Join(R(b, b))!;
        await Assert.That(joined.Minimum).IsEqualTo(b);
        await Assert.That(joined.Maximum).IsEqualTo(a);
    }

    [Test]
    public async Task Join_DateTimeBounds_TakeTheHull() {
        var d1 = new DateTime(2020, 1, 1);
        var d2 = new DateTime(2021, 1, 1);
        var d3 = new DateTime(2022, 1, 1);
        var d4 = new DateTime(2023, 1, 1);
        // #367 A-4 (authorised refinement): union on the record, hull bounds still pinned.
        var joined = (RangeConstraint)R(d1, d2).Join(R(d3, d4))!;
        await Assert.That(joined.Minimum).IsEqualTo(d1);
        await Assert.That(joined.Maximum).IsEqualTo(d4);
        await Assert.That(Same(joined, R(d1, d4))).IsFalse();
        await Assert.That(joined.Intervals!.Count).IsEqualTo(2);
        // ConstraintValidation.Evaluate cannot rate DateTime values at all (Convert.ToDouble throws, so
        // even a single-interval range is Violated for every DateTime); the exact union is pinned
        // structurally instead: the gap (d2, d3) is in no interval, both intervals are kept whole.
        await Assert.That(joined.Intervals![0]).IsEqualTo(new RangeInterval(d1, d2));
        await Assert.That(joined.Intervals![1]).IsEqualTo(new RangeInterval(d3, d4));
        await Assert.That(ConstraintValidation.IsInRangeUnion(new DateTime(2021, 6, 1), joined)).IsFalse();
    }

    [Test]
    public async Task Join_CrossNumericBounds_UseDouble() {
        await Assert.That(Same(R(1, 2).Join(R(0.5, 3.5)), R(0.5, 3.5))).IsTrue();
    }

    // ---- Length ----

    [Test]
    public async Task Join_Length_IsTheHull() {
        await Assert.That(Same(new LengthConstraint(1, 3).Join(new LengthConstraint(10, 20)), new LengthConstraint(1, 20))).IsTrue();
    }

    [Test]
    public async Task Join_Length_BoundOnOneArmOnlyDoesNotSurvive() {
        // arm A: length in [5, 8]; arm B: length in [0, 8] => no lower bound survives.
        await Assert.That(Same(new LengthConstraint(5, 8).Join(new LengthConstraint(0, 8)), new LengthConstraint(0, 8))).IsTrue();
    }

    // ---- Required / Optional ----

    [Test]
    public async Task Join_Required_SurvivesWhenBothArmsHaveIt() {
        await Assert.That(Same(new RequiredConstraint().Join(new RequiredConstraint()), new RequiredConstraint())).IsTrue();
    }

    [Test]
    public async Task Join_Optional_SurvivesWhenBothArmsHaveIt() {
        await Assert.That(Same(new OptionalConstraint().Join(new OptionalConstraint()), new OptionalConstraint())).IsTrue();
    }

    [Test]
    public async Task Join_RequiredOnOneArmOnly_DoesNotSurviveAbstractJoin() {
        var withRequired = AbstractValue.From([new RequiredConstraint()]);
        var joined = withRequired.Join(AbstractValue.Unknown);
        await Assert.That(joined.Constraints.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Join_RequiredVsOptional_DifferentKinds_NoFact() {
        await Assert.That(new RequiredConstraint().Join(new OptionalConstraint())).IsNull();
        var joined = AbstractValue.From([new RequiredConstraint()])
            .Join(AbstractValue.From([new OptionalConstraint()]));
        await Assert.That(joined.Constraints.Count).IsEqualTo(0);
    }

    // ---- Equality ----

    [Test]
    public async Task Join_Equality_SurvivesOnlyWhenEqual() {
        await Assert.That(Same(new EqualityConstraint(3).Join(new EqualityConstraint(3)), new EqualityConstraint(3))).IsTrue();
        await Assert.That(new EqualityConstraint(3).Join(new EqualityConstraint(4))).IsNull();
    }

    // ---- Pattern / Unique / Default / unknown kinds (identical-only; open question: no widening) ----

    [Test]
    public async Task Join_Pattern_SurvivesOnlyWhenIdentical_DifferentPatternsDrop() {
        await Assert.That(Same(new PatternConstraint("^a$").Join(new PatternConstraint("^a$")), new PatternConstraint("^a$"))).IsTrue();
        await Assert.That(new PatternConstraint("^a$").Join(new PatternConstraint("^b$"))).IsNull();
    }

    [Test]
    public async Task Join_Unique_SurvivesWhenBothHaveIt() {
        await Assert.That(Same(new UniqueConstraint().Join(new UniqueConstraint()), new UniqueConstraint())).IsTrue();
        var joined = AbstractValue.From([new UniqueConstraint()]).Join(AbstractValue.Unknown);
        await Assert.That(joined.Constraints.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Join_UnregisteredKind_DropsTheAxis() {
        await Assert.That(new UnregisteredConstraint().Join(new UnregisteredConstraint())).IsNull();
    }

    [Test]
    public async Task Join_DifferentConstraintTypes_NoFact() {
        await Assert.That(R(0, 1).Join(new LengthConstraint(0, 1))).IsNull();
    }

    // ---- AbstractValue ----

    [Test]
    public async Task AbstractValueJoin_DisjointRanges_IsHullAndSatisfiable() {
        var a = AbstractValue.From([R(0, 10)]);
        var b = AbstractValue.From([R(20, 30)]);
        var joined = a.Join(b);
        await Assert.That(joined.Unsatisfiable).IsFalse();
        // #367 A-4 (authorised refinement): union on the record, hull still pinned.
        var range = joined.Range!;
        await Assert.That(range.Minimum).IsEqualTo(0);
        await Assert.That(range.Maximum).IsEqualTo(30);
        await Assert.That(Same(range, R(0, 30))).IsFalse();
        await Assert.That(ConstraintValidation.Evaluate(range, 15)).IsEqualTo(ConstraintVerdict.Violated);
        await Assert.That(ConstraintValidation.Evaluate(range, 5)).IsEqualTo(ConstraintVerdict.Satisfied);
        await Assert.That(ConstraintValidation.Evaluate(range, 25)).IsEqualTo(ConstraintVerdict.Satisfied);
    }

    [Test]
    public async Task AbstractValueJoin_KeepsOnlyAxesBothArmsCarry() {
        var a = AbstractValue.From([R(0, 10), new RequiredConstraint()]);
        var b = AbstractValue.From([R(5, 20)]);
        var joined = a.Join(b);
        await Assert.That(joined.Constraints.Count).IsEqualTo(1);
        await Assert.That(Same(joined.Range, R(0, 20))).IsTrue();
    }

    [Test]
    public async Task AbstractValueJoin_UnsatisfiableIsBottom_Identity() {
        var bottom = AbstractValue.From([R(0, 1), R(5, 6)]);
        var a = AbstractValue.From([R(0, 10)]);
        await Assert.That(bottom.Unsatisfiable).IsTrue();
        await Assert.That(bottom.Join(a)).IsSameReferenceAs(a);
        await Assert.That(a.Join(bottom)).IsSameReferenceAs(a);
    }

    [Test]
    public async Task AbstractValueJoin_UnknownIsTop_Absorbs() {
        var a = AbstractValue.From([R(0, 10)]);
        await Assert.That(a.Join(AbstractValue.Unknown)).IsSameReferenceAs(AbstractValue.Unknown);
        await Assert.That(AbstractValue.Unknown.Join(a)).IsSameReferenceAs(AbstractValue.Unknown);
    }

    [Test]
    public async Task AbstractValueJoin_MixedTypeBounds_DoesNotThrow() {
        var a = AbstractValue.From([R("a", "b")]);
        var b = AbstractValue.From([R(1, 2)]);
        var joined = a.Join(b);
        await Assert.That(joined.Constraints.Count).IsEqualTo(0);
        await Assert.That(joined.Unsatisfiable).IsFalse();
    }

    // ---- Join vs meet are provably different ----

    [Test]
    public async Task JoinVsMeet_DisjointRanges_MeetEmpty_JoinHull() {
        var a = AbstractValue.From([R(0, 10)]);
        var b = AbstractValue.From([R(20, 30)]);
        await Assert.That(a.Merge(b).Unsatisfiable).IsTrue();
        await Assert.That(R(0, 10).MergeResult(R(20, 30)).IsEmpty).IsTrue();
        var joined = a.Join(b);
        await Assert.That(joined.Unsatisfiable).IsFalse();
        // #367 A-4 (authorised refinement): union on the record, hull still pinned.
        var range = joined.Range!;
        await Assert.That(range.Minimum).IsEqualTo(0);
        await Assert.That(range.Maximum).IsEqualTo(30);
        await Assert.That(Same(range, R(0, 30))).IsFalse();
        await Assert.That(ConstraintValidation.Evaluate(range, 15)).IsEqualTo(ConstraintVerdict.Violated);
        await Assert.That(ConstraintValidation.Evaluate(range, 5)).IsEqualTo(ConstraintVerdict.Satisfied);
        await Assert.That(ConstraintValidation.Evaluate(range, 25)).IsEqualTo(ConstraintVerdict.Satisfied);
    }

    [Test]
    public async Task JoinVsMeet_OneArmNarrows_MeetKeepsIt_JoinDoesNot() {
        var narrowed = AbstractValue.From([R(5, null)]);
        var plain = AbstractValue.From([R(null, 100)]);
        // meet keeps the x >= 5 narrowing
        await Assert.That(Same(narrowed.Merge(plain).Range, R(5, 100))).IsTrue();
        // join must not: no bound is common to both arms, so the whole axis drops
        var joined = narrowed.Join(plain);
        await Assert.That(joined.Constraints.Count).IsEqualTo(0);
    }

    [Test]
    public async Task JoinVsMeet_OneArmNarrowsRange_JoinDropsNarrowing() {
        var narrowed = AbstractValue.From([R(5, 10)]);
        var wide = AbstractValue.From([R(0, 10)]);
        await Assert.That(Same(narrowed.Merge(wide).Range, R(5, 10))).IsTrue();
        await Assert.That(Same(narrowed.Join(wide).Range, R(0, 10))).IsTrue();
    }
}