using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #367 A-5: independently-typed axes compose. Presence (Required/Optional) and each value
/// kind are separate axes of one <see cref="AbstractValue"/>; a kind whose constraints do not
/// collapse to one (two different patterns) is a conjunction, never a false bottom.
/// </summary>
public class ConstraintProductTests {
    private static ConstraintVerdict Verdict(AbstractValue v, object? literal) {
        var any = ConstraintVerdict.Satisfied;
        foreach (var c in v.Constraints) {
            var r = ConstraintValidation.Evaluate(c, literal);
            if (r == ConstraintVerdict.Violated) return ConstraintVerdict.Violated;
            if (r == ConstraintVerdict.Undecided) any = ConstraintVerdict.Undecided;
        }
        return any;
    }

    // ---- (b) two same-kind constraints that both hold ----

    [Test]
    public async Task From_TwoDifferentPatterns_AreAConjunctionNotUnsatisfiable() {
        var v = AbstractValue.From([new PatternConstraint("^a"), new PatternConstraint("b$")]);
        await Assert.That(v.Unsatisfiable).IsFalse();
        await Assert.That(Verdict(v, "ab")).IsEqualTo(ConstraintVerdict.Satisfied);
        await Assert.That(Verdict(v, "a")).IsEqualTo(ConstraintVerdict.Violated);
        await Assert.That(Verdict(v, "b")).IsEqualTo(ConstraintVerdict.Violated);
    }

    [Test]
    public async Task Narrow_SecondPattern_KeepsBothEnforced() {
        var v = AbstractValue.From([new PatternConstraint("^a")]).Narrow(new PatternConstraint("b$"));
        await Assert.That(v.Unsatisfiable).IsFalse();
        await Assert.That(Verdict(v, "ab")).IsEqualTo(ConstraintVerdict.Satisfied);
        await Assert.That(Verdict(v, "a")).IsEqualTo(ConstraintVerdict.Violated);
        await Assert.That(Verdict(v, "b")).IsEqualTo(ConstraintVerdict.Violated);
    }

    [Test]
    public async Task Merge_PatternConjunction_KeepsBothEnforced() {
        var v = AbstractValue.From([new PatternConstraint("^a")]).Merge(AbstractValue.From([new PatternConstraint("b$")]));
        await Assert.That(v.Unsatisfiable).IsFalse();
        await Assert.That(Verdict(v, "a")).IsEqualTo(ConstraintVerdict.Violated);
    }

    [Test]
    public async Task From_IdenticalPatterns_StayOne() {
        var v = AbstractValue.From([new PatternConstraint("^a"), new PatternConstraint("^a")]);
        await Assert.That(v.Constraints.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Join_PatternConjunctions_KeepsOnlyWhatBothArmsHold() {
        var left = AbstractValue.From([new PatternConstraint("^a"), new PatternConstraint("b$")]);
        var right = AbstractValue.From([new PatternConstraint("b$")]);
        var joined = left.Join(right);
        await Assert.That(joined.Constraints.Count).IsEqualTo(1);
        await Assert.That(((PatternConstraint)joined.Constraints[0]).Pattern).IsEqualTo("b$");
        // "b" holds on the right arm, so the join must not demand ^a.
        await Assert.That(Verdict(joined, "b")).IsEqualTo(ConstraintVerdict.Satisfied);
    }

    // ---- (a) presence axis (already correct; pinned) ----

    [Test]
    public async Task Join_RequiredAndOptional_IsUnknownPresence() {
        var joined = AbstractValue.From([new RequiredConstraint()]).Join(AbstractValue.From([new OptionalConstraint()]));
        await Assert.That(joined.Constraints.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Join_RequiredRequired_StaysRequired() {
        var joined = AbstractValue.From([new RequiredConstraint()]).Join(AbstractValue.From([new RequiredConstraint()]));
        await Assert.That(joined.Constraints.Single()).IsTypeOf<RequiredConstraint>();
    }

    [Test]
    public async Task Join_OptionalOptional_StaysOptional() {
        var joined = AbstractValue.From([new OptionalConstraint()]).Join(AbstractValue.From([new OptionalConstraint()]));
        await Assert.That(joined.Constraints.Single()).IsTypeOf<OptionalConstraint>();
    }

    // ---- axes compose ----

    [Test]
    public async Task RequiredWithRange_BothAxesEnforced() {
        var v = AbstractValue.From([new RequiredConstraint(), new RangeConstraint(0, 10)]);
        await Assert.That(Verdict(v, 5)).IsEqualTo(ConstraintVerdict.Satisfied);
        await Assert.That(Verdict(v, null)).IsEqualTo(ConstraintVerdict.Violated);
        await Assert.That(Verdict(v, 50)).IsEqualTo(ConstraintVerdict.Violated);
    }

    [Test]
    public async Task RequiredWithRange_JoinChangesEachAxisIndependently() {
        var a = AbstractValue.From([new RequiredConstraint(), new RangeConstraint(0, 10)]);
        var b = AbstractValue.From([new OptionalConstraint(), new RangeConstraint(20, 30)]);
        var joined = a.Join(b);
        await Assert.That(joined.Constraints.OfType<RequiredConstraint>().Any()).IsFalse();
        await Assert.That(joined.Constraints.OfType<OptionalConstraint>().Any()).IsFalse();
        await Assert.That(joined.Range).IsNotNull();
        await Assert.That(joined.Range!.Minimum).IsEqualTo(0);
        await Assert.That(joined.Range!.Maximum).IsEqualTo(30);
    }

    [Test]
    public async Task RequiredWithRange_MeetNarrowsRangeKeepsPresence() {
        var v = AbstractValue.From([new RequiredConstraint(), new RangeConstraint(0, 10)]).Narrow(new RangeConstraint(5, null));
        await Assert.That(v.Constraints.OfType<RequiredConstraint>().Count()).IsEqualTo(1);
        await Assert.That(v.Range!.Minimum).IsEqualTo(5);
        await Assert.That(v.Range!.Maximum).IsEqualTo(10);
    }
}