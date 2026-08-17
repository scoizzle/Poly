using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Lowering;

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
        new LoweringContext(new Parameter("entity"), Meaning: ExtensionCatalog.Core.Language.Meaning));

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