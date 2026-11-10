using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>#367 A-6: lattice rules and fail-closed verdicts of <see cref="RelationalConstraint"/>.</summary>
public class RelationalConstraintLatticeTests {
    private const RelationalOrdering Lt = RelationalOrdering.Less;
    private const RelationalOrdering Eq = RelationalOrdering.Equal;
    private const RelationalOrdering Gt = RelationalOrdering.Greater;

    private static RelationalConstraint Rel(RelationalOrdering o, string other = "End") => new(other, o);

    // Records carry a node Id, so == would never hold; compare through the registry's Identical seam.
    private static bool Same(Constraint? actual, Constraint expected) =>
        actual is not null && ConstraintMerge.Identical(actual, expected);

    [Test]
    public async Task Merge_SamePair_IntersectsOrderings() {
        var lessOrEqual = Rel(Lt | Eq);
        var greaterOrEqual = Rel(Gt | Eq);
        var net = lessOrEqual.MergeResult(greaterOrEqual);
        await Assert.That(net.Kind).IsEqualTo(ConstraintMergeKind.Net);
        await Assert.That(((RelationalConstraint)net.Net!).Orderings).IsEqualTo(Eq);
        await Assert.That(Same(Rel(Lt | Eq).MergeResult(Rel(Lt)).Net, Rel(Lt))).IsTrue();
    }

    [Test]
    public async Task Merge_ContradictoryOrderings_IsEmpty() {
        await Assert.That(Rel(Lt).MergeResult(Rel(Gt)).Kind).IsEqualTo(ConstraintMergeKind.Empty);
        await Assert.That(Rel(Lt).MergeResult(Rel(Eq)).Kind).IsEqualTo(ConstraintMergeKind.Empty);
    }

    [Test]
    public async Task Merge_DifferentPairs_IsUnmergeable_NotEmpty() {
        await Assert.That(Rel(Lt, "End").MergeResult(Rel(Gt, "Other")).Kind).IsEqualTo(ConstraintMergeKind.Unmergeable);
    }

    [Test]
    public async Task Join_SamePair_UnionsOrderings() {
        await Assert.That(Same(Rel(Lt).Join(Rel(Eq)), Rel(Lt | Eq))).IsTrue();
        await Assert.That(Same(Rel(Lt).Join(Rel(Gt)), Rel(Lt | Gt))).IsTrue();
    }

    [Test]
    public async Task Join_ToNoInformationOrDifferentPair_DropsTheAxis() {
        await Assert.That(Rel(Lt | Eq).Join(Rel(Gt))).IsNull();
        await Assert.That(Rel(Lt, "End").Join(Rel(Lt, "Other"))).IsNull();
    }

    [Test]
    public async Task Identical_RequiresSamePairAndOrderings() {
        await Assert.That(ConstraintMerge.Identical(Rel(Lt), Rel(Lt))).IsTrue();
        await Assert.That(ConstraintMerge.Identical(Rel(Lt), Rel(Lt | Eq))).IsFalse();
        await Assert.That(ConstraintMerge.Identical(Rel(Lt, "End"), Rel(Lt, "Other"))).IsFalse();
    }

    [Test]
    public async Task Evaluate_AgainstALiteral_IsAlwaysUndecided() {
        foreach (var literal in new object?[] { 1, "x", null })
            await Assert.That(ConstraintValidation.Evaluate(Rel(Lt), literal)).IsEqualTo(ConstraintVerdict.Undecided);
    }

    [Test]
    public async Task Describe_AndPrint_HaveNoSilentFallthrough() {
        await Assert.That(ConstraintValidation.Describe(Rel(Lt | Eq))).IsEqualTo("<= End");
        await Assert.That(ConstraintKinds.Of(Rel(Lt))!.PrintedInDsl).IsFalse();
        await Assert.That(new DomainDslPrinter().PrintConstraintText(Rel(Lt))).IsEqualTo("?RelationalConstraint");
    }

    [Test]
    public async Task Mirror_SwapsLessAndGreater() {
        await Assert.That(Same(Rel(Lt | Eq).MirroredFrom("Start"), new RelationalConstraint("Start", Gt | Eq))).IsTrue();
        await Assert.That(Same(Rel(Lt | Gt).MirroredFrom("Start"), new RelationalConstraint("Start", Lt | Gt))).IsTrue();
    }

    [Test]
    public async Task Decide_DisjointRanges_IsViolated() {
        var verdict = ConstraintValidation.DecideRelation(Rel(Lt), new RangeConstraint(10, 20), new RangeConstraint(0, 5));
        await Assert.That(verdict).IsEqualTo(ConstraintVerdict.Violated);
    }

    [Test]
    public async Task Decide_RangesThatForceTheRelation_IsSatisfied() {
        var verdict = ConstraintValidation.DecideRelation(Rel(Lt), new RangeConstraint(0, 4), new RangeConstraint(5, 9));
        await Assert.That(verdict).IsEqualTo(ConstraintVerdict.Satisfied);
    }

    [Test]
    public async Task Decide_TouchingBounds_StrictIsViolatedNonStrictIsUndecided() {
        // Start in [5, 9], End in [0, 5]: Start < End is impossible; Start <= End only at 5 == 5.
        var start = new RangeConstraint(5, 9);
        var end = new RangeConstraint(0, 5);
        await Assert.That(ConstraintValidation.DecideRelation(Rel(Lt), start, end)).IsEqualTo(ConstraintVerdict.Violated);
        await Assert.That(ConstraintValidation.DecideRelation(Rel(Lt | Eq), start, end)).IsEqualTo(ConstraintVerdict.Undecided);
    }

    [Test]
    public async Task Decide_UnboundedOrMissingRanges_IsUndecided_NeverSatisfied() {
        await Assert.That(ConstraintValidation.DecideRelation(Rel(Lt), null, null)).IsEqualTo(ConstraintVerdict.Undecided);
        await Assert.That(ConstraintValidation.DecideRelation(Rel(Lt), new RangeConstraint(0, null), new RangeConstraint(null, 5)))
            .IsEqualTo(ConstraintVerdict.Undecided);
    }

    [Test]
    public async Task Decide_UnorderableBounds_IsUndecided() {
        var verdict = ConstraintValidation.DecideRelation(Rel(Lt), new RangeConstraint("a", "b"), new RangeConstraint(1, 2));
        await Assert.That(verdict).IsEqualTo(ConstraintVerdict.Undecided);
    }

    [Test]
    public async Task AbstractValue_KeepsRelationsToDifferentPairs_AndContradictionIsUnsatisfiable() {
        var both = AbstractValue.From([Rel(Lt, "End"), Rel(Gt, "Other")]);
        await Assert.That(both.Constraints.OfType<RelationalConstraint>().Count()).IsEqualTo(2);
        await Assert.That(both.Unsatisfiable).IsFalse();
        await Assert.That(both.Narrow(Rel(Gt, "End")).Unsatisfiable).IsTrue();
    }

    [Test]
    public async Task AbstractValue_Join_KeepsOnlyWhatBothArmsHold() {
        var a = AbstractValue.From([Rel(Lt, "End")]);
        var b = AbstractValue.From([Rel(Eq, "End")]);
        await Assert.That(Same(a.Join(b).Constraints.Single(), Rel(Lt | Eq))).IsTrue();
        await Assert.That(a.Join(AbstractValue.Unknown).Constraints.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AbstractValue_WithoutRelationsTo_RemovesOnlyThatPair() {
        var v = AbstractValue.From([Rel(Lt, "End"), Rel(Lt, "Other")]).WithoutRelationsTo("End");
        await Assert.That(Same(v.Constraints.Single(), Rel(Lt, "Other"))).IsTrue();
    }
}