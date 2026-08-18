using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// p1-1: clock <c>Now</c>/<c>Today</c> expression IR via Temporal folds.
/// With the form registered, identifiers <c>Now</c>/<c>today</c> fold to the
/// dedicated clock nodes and lower to CLR clock members; without the form they
/// stay <see cref="PropertyAccess"/> (back-compat; pack-absent analysis is task 4).
/// </summary>
public class NowExpressionFormTests {
    private static DomainSession NowFormInputs() =>
        ExtensionCatalog.Core.Language;

    private readonly DomainExpressionLoweringPass Pass = new(
        new LoweringContext(new Parameter("entity"), Meaning: ExtensionCatalog.Core.Language.Meaning));

    [Test]
    public async Task Now_Form_ParsesAndLowers() {
        var expr = DslExpressionFragment.ParseExpressionFragment("Now", NowFormInputs());

        await Assert.That(expr).IsTypeOf<Now>();

        var lowered = Pass.Lower(expr, new ParameterReference());
        await Assert.That(lowered).IsTypeOf<Member>();
        var member = (Member)lowered;
        await Assert.That(member.MemberName).IsEqualTo("UtcNow");
        await Assert.That(member.Value).IsTypeOf<NamedTypeReference>();
        await Assert.That(((NamedTypeReference)member.Value).TypeName).IsEqualTo("DateTime");
    }

    [Test]
    public async Task Now_WithoutForm_IsPropertyAccess() {
        var expr = DslExpressionFragment.ParseExpressionFragment("Now");

        await Assert.That(expr).IsTypeOf<PropertyAccess>();
        await Assert.That(((PropertyAccess)expr).Name).IsEqualTo("Now");
    }

    [Test]
    public async Task Today_Form_ParsesAndLowers() {
        var expr = DslExpressionFragment.ParseExpressionFragment("Today", NowFormInputs());

        await Assert.That(expr).IsTypeOf<Today>();

        var lowered = Pass.Lower(expr, new ParameterReference());
        await Assert.That(lowered).IsNotNull();
    }

    [Test]
    public async Task Today_WithoutForm_IsPropertyAccess() {
        var expr = DslExpressionFragment.ParseExpressionFragment("Today");

        await Assert.That(expr).IsTypeOf<PropertyAccess>();
        await Assert.That(((PropertyAccess)expr).Name).IsEqualTo("Today");
    }

    [Test]
    public async Task Today_Lowercase_IsNotClock() {
        var expr = DslExpressionFragment.ParseExpressionFragment("today", NowFormInputs());

        await Assert.That(expr).IsTypeOf<PropertyAccess>();
        await Assert.That(((PropertyAccess)expr).Name).IsEqualTo("today");
    }
}