using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// p1-2: duration primary forms (<c>12 Days</c> / <c>3 Months</c>, singular and
/// plural) fold to a <see cref="Duration"/> node when <see cref="DurationForm"/> is
/// registered, and <c>Now - 12 Days</c> folds to a <see cref="DateOperation"/> at
/// parse. Unknown units (<c>12 fortnights</c>) fail closed — never a DateOperation.
/// </summary>
public class DurationExpressionFormTests {
    private static DomainSession DurationInputs() =>
        ExtensionCatalog.Core.Language;

    private static DomainSession TemporalInputs() =>
        ExtensionCatalog.Core.Language;

    private readonly DomainExpressionLoweringPass Pass = new(
        new LoweringContext(new Parameter("entity")));

    [Test]
    public async Task Duration_12Days_Form_Parses() {
        var expr = DslExpressionFragment.ParseExpressionFragment("12 Days", DurationInputs());

        await Assert.That(expr).IsTypeOf<Duration>();
        var duration = (Duration)expr;
        await Assert.That(duration.Amount).IsEqualTo(12);
        await Assert.That(duration.Unit).IsEqualTo(DurationUnit.Days);
    }

    [Test]
    public async Task Duration_3Months_Form_Parses() {
        var expr = DslExpressionFragment.ParseExpressionFragment("3 Months", DurationInputs());

        await Assert.That(expr).IsTypeOf<Duration>();
        var duration = (Duration)expr;
        await Assert.That(duration.Amount).IsEqualTo(3);
        await Assert.That(duration.Unit).IsEqualTo(DurationUnit.Months);
    }

    [Test]
    public async Task Duration_SingularUnits_Form_Parses() {
        var expr = DslExpressionFragment.ParseExpressionFragment("1 Day", DurationInputs());

        await Assert.That(expr).IsTypeOf<Duration>();
        var duration = (Duration)expr;
        await Assert.That(duration.Amount).IsEqualTo(1);
        await Assert.That(duration.Unit).IsEqualTo(DurationUnit.Days);
    }

    [Test]
    [Arguments("5 ms", DurationUnit.Milliseconds)]
    [Arguments("5 Milliseconds", DurationUnit.Milliseconds)]
    [Arguments("5 Seconds", DurationUnit.Seconds)]
    [Arguments("5 Minutes", DurationUnit.Minutes)]
    [Arguments("5 Hours", DurationUnit.Hours)]
    [Arguments("5 Days", DurationUnit.Days)]
    [Arguments("5 Weeks", DurationUnit.Weeks)]
    [Arguments("5 Months", DurationUnit.Months)]
    [Arguments("5 Years", DurationUnit.Years)]
    [Arguments("1 Millisecond", DurationUnit.Milliseconds)]
    [Arguments("1 Second", DurationUnit.Seconds)]
    [Arguments("1 Minute", DurationUnit.Minutes)]
    [Arguments("1 Hour", DurationUnit.Hours)]
    [Arguments("1 Week", DurationUnit.Weeks)]
    [Arguments("1 Year", DurationUnit.Years)]
    public async Task Duration_CommonUnits_Form_Parses(string spelling, DurationUnit unit) {
        var expr = DslExpressionFragment.ParseExpressionFragment(spelling, DurationInputs());

        await Assert.That(expr).IsTypeOf<Duration>();
        var duration = (Duration)expr;
        await Assert.That(duration.Amount).IsEqualTo(spelling.StartsWith("1 ", StringComparison.Ordinal) ? 1L : 5L);
        await Assert.That(duration.Unit).IsEqualTo(unit);
    }

    [Test]
    public async Task Now_Minus_12Days_BecomesDateOperation() {
        var expr = DslExpressionFragment.ParseExpressionFragment("Now - 12 Days", TemporalInputs());

        await Assert.That(expr).IsTypeOf<DateOperation>();
        var dateOp = (DateOperation)expr;
        await Assert.That(dateOp.Date).IsTypeOf<Now>();
        await Assert.That(dateOp.Kind).IsEqualTo(DateOperationKind.AddDays);
        await Assert.That(dateOp.Offset).IsTypeOf<Literal>();
        await Assert.That(((Literal)dateOp.Offset).Value).IsEqualTo(-12L);

        var lowered = Pass.Lower(expr, new ParameterReference());
        await Assert.That(lowered).IsTypeOf<Invoke>();
        var invoke = (Invoke)lowered;
        await Assert.That(invoke.Delegate).IsTypeOf<Member>();
        await Assert.That(((Member)invoke.Delegate).MemberName).IsEqualTo("AddDays");
        await Assert.That(invoke.Arguments.Length).IsEqualTo(1);
        await Assert.That(invoke.Arguments[0]).IsTypeOf<Constant>();
        await Assert.That(((Constant)invoke.Arguments[0]).Value).IsEqualTo(-12L);
    }

    [Test]
    public async Task Now_Minus_50Ms_BecomesDateOperation_AddMilliseconds() {
        var expr = DslExpressionFragment.ParseExpressionFragment("Now - 50 ms", TemporalInputs());

        await Assert.That(expr).IsTypeOf<DateOperation>();
        var dateOp = (DateOperation)expr;
        await Assert.That(dateOp.Kind).IsEqualTo(DateOperationKind.AddMilliseconds);
        await Assert.That(((Literal)dateOp.Offset).Value).IsEqualTo(-50L);

        var lowered = Pass.Lower(expr, new ParameterReference());
        await Assert.That(((Member)((Invoke)lowered).Delegate).MemberName).IsEqualTo("AddMilliseconds");
        await Assert.That(((Constant)((Invoke)lowered).Arguments[0]).Value).IsEqualTo(-50L);
    }

    [Test]
    public async Task Now_Minus_2Hours_BecomesDateOperation_AddHours() {
        var expr = DslExpressionFragment.ParseExpressionFragment("Now - 2 Hours", TemporalInputs());

        await Assert.That(expr).IsTypeOf<DateOperation>();
        var dateOp = (DateOperation)expr;
        await Assert.That(dateOp.Date).IsTypeOf<Now>();
        await Assert.That(dateOp.Kind).IsEqualTo(DateOperationKind.AddHours);
        await Assert.That(((Literal)dateOp.Offset).Value).IsEqualTo(-2L);

        var lowered = Pass.Lower(expr, new ParameterReference());
        await Assert.That(((Member)((Invoke)lowered).Delegate).MemberName).IsEqualTo("AddHours");
        await Assert.That(((Constant)((Invoke)lowered).Arguments[0]).Value).IsEqualTo(-2L);
    }

    [Test]
    public async Task Now_Plus_2Weeks_LowersToAddDays_Scaled() {
        var expr = DslExpressionFragment.ParseExpressionFragment("Now + 2 Weeks", TemporalInputs());

        await Assert.That(expr).IsTypeOf<DateOperation>();
        var dateOp = (DateOperation)expr;
        await Assert.That(dateOp.Kind).IsEqualTo(DateOperationKind.AddWeeks);
        await Assert.That(((Literal)dateOp.Offset).Value).IsEqualTo(2L);

        var lowered = Pass.Lower(expr, new ParameterReference());
        await Assert.That(((Member)((Invoke)lowered).Delegate).MemberName).IsEqualTo("AddDays");
        await Assert.That(((Constant)((Invoke)lowered).Arguments[0]).Value).IsEqualTo(14L);
    }

    [Test]
    public async Task Today_Plus_1Year_BecomesDateOperation_AddYears() {
        var expr = DslExpressionFragment.ParseExpressionFragment("Today + 1 Year", TemporalInputs());

        await Assert.That(expr).IsTypeOf<DateOperation>();
        var dateOp = (DateOperation)expr;
        await Assert.That(dateOp.Date).IsTypeOf<Today>();
        await Assert.That(dateOp.Kind).IsEqualTo(DateOperationKind.AddYears);

        var lowered = Pass.Lower(expr, new ParameterReference());
        await Assert.That(((Member)((Invoke)lowered).Delegate).MemberName).IsEqualTo("AddYears");
    }

    [Test]
    public async Task Now_Plus_2Hours_Plus_3Minutes_NestsDateOperations() {
        var expr = DslExpressionFragment.ParseExpressionFragment(
            "Now + 2 Hours + 3 Minutes", TemporalInputs());

        await Assert.That(expr).IsTypeOf<DateOperation>();
        var outer = (DateOperation)expr;
        await Assert.That(outer.Kind).IsEqualTo(DateOperationKind.AddMinutes);
        await Assert.That(((Literal)outer.Offset).Value).IsEqualTo(3L);
        await Assert.That(outer.Date).IsTypeOf<DateOperation>();
        var inner = (DateOperation)outer.Date;
        await Assert.That(inner.Kind).IsEqualTo(DateOperationKind.AddHours);
        await Assert.That(((Literal)inner.Offset).Value).IsEqualTo(2L);
        await Assert.That(inner.Date).IsTypeOf<Now>();

        var lowered = Pass.Lower(expr, new ParameterReference());
        await Assert.That(lowered).IsTypeOf<Invoke>();
        var outerInvoke = (Invoke)lowered;
        await Assert.That(((Member)outerInvoke.Delegate).MemberName).IsEqualTo("AddMinutes");
        await Assert.That(((Constant)outerInvoke.Arguments[0]).Value).IsEqualTo(3L);
        await Assert.That(((Member)outerInvoke.Delegate).Value).IsTypeOf<Invoke>();
        var innerInvoke = (Invoke)((Member)outerInvoke.Delegate).Value;
        await Assert.That(((Member)innerInvoke.Delegate).MemberName).IsEqualTo("AddHours");
        await Assert.That(((Constant)innerInvoke.Arguments[0]).Value).IsEqualTo(2L);
    }

    [Test]
    public async Task Duration_LowercaseUnit_IsNotDuration() {
        await Assert.That(() => DslExpressionFragment.ParseExpressionFragment("12 days", DurationInputs()))
            .Throws<FormatException>();
    }

    [Test]
    public async Task UnknownUnit_Fortnights_DoesNotSucceedAsTemporal() {
        await Assert.That(() => DslExpressionFragment.ParseExpressionFragment("12 fortnights", DurationInputs()))
            .Throws<FormatException>();

        await Assert.That(() => DslExpressionFragment.ParseExpressionFragment("Now - 12 Fortnights", TemporalInputs()))
            .Throws<FormatException>();
    }
}