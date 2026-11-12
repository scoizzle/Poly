using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #367: range validation compares bounds type-correctly (ConstraintMerge.TryCompare's rule):
/// same type by its own ordering, two numeric types exactly through ExactNumber, anything else is
/// not coerced. A value that is not exactly representable (a fractional double) is unorderable.
/// </summary>
public class ConstraintRangeValidationTests {
    private static ConstraintVerdict Eval(object? min, object? max, object? value) =>
        ConstraintValidation.Evaluate(new RangeConstraint(min, max), value);

    private static readonly DateTime D1 = new(2020, 1, 1);
    private static readonly DateTime D2 = new(2021, 1, 1);

    [Test]
    public async Task DateTime_StrictlyInsideRange_IsSatisfied() {
        await Assert.That(Eval(D1, D2, new DateTime(2020, 6, 1))).IsEqualTo(ConstraintVerdict.Satisfied);
    }

    [Test]
    public async Task DateTime_OnEitherBound_IsSatisfied() {
        await Assert.That(Eval(D1, D2, D1)).IsEqualTo(ConstraintVerdict.Satisfied);
        await Assert.That(Eval(D1, D2, D2)).IsEqualTo(ConstraintVerdict.Satisfied);
    }

    [Test]
    public async Task DateTime_BelowMinimumOrAboveMaximum_IsViolated() {
        await Assert.That(Eval(D1, D2, D1.AddTicks(-1))).IsEqualTo(ConstraintVerdict.Violated);
        await Assert.That(Eval(D1, D2, D2.AddTicks(1))).IsEqualTo(ConstraintVerdict.Violated);
    }

    [Test]
    public async Task DateTime_OneSidedRanges_Behave() {
        await Assert.That(Eval(D1, null, D2)).IsEqualTo(ConstraintVerdict.Satisfied);
        await Assert.That(Eval(D1, null, D1.AddDays(-1))).IsEqualTo(ConstraintVerdict.Violated);
        await Assert.That(Eval(null, D2, D1)).IsEqualTo(ConstraintVerdict.Satisfied);
        await Assert.That(Eval(null, D2, D2.AddDays(1))).IsEqualTo(ConstraintVerdict.Violated);
    }

    [Test]
    public async Task Decimal_PrecisionBeyondDouble_IsPreserved() {
        var max = 9007199254740992m;
        var above = 9007199254740993m; // same double as max
        await Assert.That(Eval(null, max, above)).IsEqualTo(ConstraintVerdict.Violated);
        await Assert.That(Eval(null, max, max)).IsEqualTo(ConstraintVerdict.Satisfied);
        await Assert.That(Eval(above, null, max)).IsEqualTo(ConstraintVerdict.Violated);
    }

    [Test]
    public async Task CrossNumeric_ComparesExactly() {
        // V-05a: two numeric types compare through ExactNumber, so a fractional decimal is exact.
        await Assert.That(Eval(1, 10, 5.5m)).IsEqualTo(ConstraintVerdict.Satisfied);
        // A fractional double is not exactly representable, so it is unorderable: fail closed.
        // (Before V-05a this compared as double and was Satisfied.)
        await Assert.That(Eval(1, 10, 5.5)).IsEqualTo(ConstraintVerdict.Violated);
        await Assert.That(Eval(1.5, 10, 1)).IsEqualTo(ConstraintVerdict.Violated);
        await Assert.That(Eval(0L, 10L, 5)).IsEqualTo(ConstraintVerdict.Satisfied);
    }

    [Test]
    public async Task DateTimeValue_AgainstNumberRange_IsNotCoerced_Violated() {
        await Assert.That(Eval(1, 10, new DateTime(2020, 1, 1))).IsEqualTo(ConstraintVerdict.Violated);
    }

    [Test]
    public async Task NumberValue_AgainstDateTimeRange_IsNotCoerced_Violated() {
        // The old validator silently dropped the unconvertible bound, so this was Satisfied.
        await Assert.That(Eval(D1, D2, 5)).IsEqualTo(ConstraintVerdict.Violated);
    }

    [Test]
    public async Task NullAndNonComparableValues_AreViolated() {
        await Assert.That(Eval(1, 10, null)).IsEqualTo(ConstraintVerdict.Violated);
        await Assert.That(Eval(1, 10, new object())).IsEqualTo(ConstraintVerdict.Violated);
    }

    [Test]
    public async Task DateTimeUnion_ValuesInEachIntervalSatisfied_GapViolated() {
        var d3 = new DateTime(2022, 1, 1);
        var d4 = new DateTime(2023, 1, 1);
        var joined = (RangeConstraint)new RangeConstraint(D1, D2).Join(new RangeConstraint(d3, d4))!;
        await Assert.That(ConstraintValidation.Evaluate(joined, new DateTime(2020, 6, 1))).IsEqualTo(ConstraintVerdict.Satisfied);
        await Assert.That(ConstraintValidation.Evaluate(joined, new DateTime(2022, 6, 1))).IsEqualTo(ConstraintVerdict.Satisfied);
        await Assert.That(ConstraintValidation.Evaluate(joined, new DateTime(2021, 6, 1))).IsEqualTo(ConstraintVerdict.Violated);
    }
}