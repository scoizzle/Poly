using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #367 A-4: a join of disjoint ranges is a normalised union of intervals; the record keeps the
/// HULL in Minimum/Maximum so every reader that can only see one interval stays sound.
/// </summary>
public class ConstraintUnionTests {
    private static RangeConstraint R(object? min, object? max) => new(min, max);

    private static RangeConstraint Union(RangeConstraint a, RangeConstraint b) => (RangeConstraint)a.Join(b)!;

    private static IReadOnlyList<(object? Min, object? Max)> Parts(RangeConstraint r) =>
        r.Intervals is null
            ? [(r.Minimum, r.Maximum)]
            : [.. r.Intervals.Select(i => (i.Minimum, i.Maximum))];

    [Test]
    public async Task Evaluate_JoinOfDisjointRanges_GapIsViolated() {
        var union = R(0, 10).Join(R(20, 30))!;
        await Assert.That(ConstraintValidation.Evaluate(union, 15)).IsEqualTo(ConstraintVerdict.Violated);
    }

    [Test]
    public async Task Evaluate_Union_ValueInAnyIntervalIsSatisfied() {
        var union = R(0, 10).Join(R(20, 30))!;
        await Assert.That(ConstraintValidation.Evaluate(union, 5)).IsEqualTo(ConstraintVerdict.Satisfied);
        await Assert.That(ConstraintValidation.Evaluate(union, 20)).IsEqualTo(ConstraintVerdict.Satisfied);
        await Assert.That(ConstraintValidation.Evaluate(union, 30)).IsEqualTo(ConstraintVerdict.Satisfied);
        await Assert.That(ConstraintValidation.Evaluate(union, 31)).IsEqualTo(ConstraintVerdict.Violated);
        await Assert.That(ConstraintValidation.Evaluate(union, null)).IsEqualTo(ConstraintVerdict.Violated);
    }

    [Test]
    public async Task Join_DisjointRanges_IsUnionWithHullOnTheRecord() {
        var union = Union(R(0, 10), R(20, 30));
        await Assert.That(Parts(union).SequenceEqual([((object?)0, (object?)10), (20, 30)])).IsTrue();
        await Assert.That(union.Minimum).IsEqualTo(0);
        await Assert.That(union.Maximum).IsEqualTo(30);
    }

    [Test]
    public async Task Normalisation_AdjacentAndOverlappingIntervalsMerge() {
        var touching = Union(R(0, 10), R(10, 20));
        await Assert.That(touching.Intervals).IsNull();
        await Assert.That(ConstraintMerge.Identical(touching, R(0, 20))).IsTrue();

        var overlapping = Union(R(0, 15), R(10, 20));
        await Assert.That(overlapping.Intervals).IsNull();
        await Assert.That(ConstraintMerge.Identical(overlapping, R(0, 20))).IsTrue();

        // three arms, out of order, the middle one bridges the first two
        var three = Union(Union(R(40, 50), R(0, 10)), R(8, 12));
        await Assert.That(Parts(three).SequenceEqual([((object?)0, (object?)12), (40, 50)])).IsTrue();
    }

    [Test]
    public async Task Identical_UnionAndHull_AreNotIdentical() {
        var union = Union(R(0, 10), R(20, 30));
        await Assert.That(ConstraintMerge.Identical(union, R(0, 30))).IsFalse();
        await Assert.That(ConstraintMerge.Identical(union, Union(R(0, 10), R(20, 30)))).IsTrue();
        await Assert.That(ConstraintMerge.Identical(union, Union(R(0, 10), R(20, 31)))).IsFalse();
    }

    [Test]
    public async Task Meet_DistributesOverTwoMultiIntervalValues() {
        var a = Union(R(0, 10), R(20, 30));    // [0,10] u [20,30]
        var b = Union(R(5, 25), R(28, 40));    // [5,25] u [28,40]
        var met = a.MergeResult(b);
        await Assert.That(met.IsNet).IsTrue();
        var net = (RangeConstraint)met.Net!;
        // [5,10] u [20,25] u [28,30]
        await Assert.That(Parts(net).SequenceEqual([((object?)5, (object?)10), (20, 25), (28, 30)])).IsTrue();
        await Assert.That(net.Minimum).IsEqualTo(5);
        await Assert.That(net.Maximum).IsEqualTo(30);
    }

    [Test]
    public async Task Meet_UnionWithPlainRange_NarrowsToASingleIntervalWhenOnlyOneSurvives() {
        var a = Union(R(0, 10), R(20, 30));
        var met = a.MergeResult(R(18, 100));
        var net = (RangeConstraint)met.Net!;
        await Assert.That(net.Intervals).IsNull();
        await Assert.That(ConstraintMerge.Identical(net, R(20, 30))).IsTrue();
    }

    [Test]
    public async Task Meet_LeavingNoInterval_IsExactlyEmpty() {
        var a = Union(R(0, 10), R(20, 30));
        // the hull of a is [0,30]; 12..18 sits inside the hull but in the gap
        await Assert.That(a.MergeResult(R(12, 18)).IsEmpty).IsTrue();
        await Assert.That(AbstractValue.From([a, R(12, 18)]).Unsatisfiable).IsTrue();
        await Assert.That(AbstractValue.From([a]).Narrow(R(12, 18)).Unsatisfiable).IsTrue();
    }

    [Test]
    public async Task AbstractValueJoin_DisjointRanges_KeepsHullAndUnion() {
        var joined = AbstractValue.From([R(0, 10)]).Join(AbstractValue.From([R(20, 30)]));
        await Assert.That(joined.Unsatisfiable).IsFalse();
        await Assert.That(joined.Range!.Intervals!.Count).IsEqualTo(2);
        await Assert.That(joined.NumericRange).IsEqualTo(new ValueRange(0, 30));
    }

    [Test]
    public async Task Describe_Union_ListsEveryInterval() {
        var text = ConstraintValidation.Describe(Union(R(0, 10), R(20, 30)));
        await Assert.That(text).IsEqualTo("range(0, 10) ∪ range(20, 30)");
    }

    [Test]
    public async Task Join_UnionWithPlainRange_ExtendsTheUnion() {
        var union = Union(Union(R(0, 10), R(20, 30)), R(40, 50));
        await Assert.That(Parts(union).Count).IsEqualTo(3);
        var swallowed = Union(Union(R(0, 10), R(20, 30)), R(5, 25));
        await Assert.That(swallowed.Intervals).IsNull();
        await Assert.That(ConstraintMerge.Identical(swallowed, R(0, 30))).IsTrue();
    }
}