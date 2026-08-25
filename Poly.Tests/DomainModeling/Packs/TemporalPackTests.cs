using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Packs;

/// <summary>
/// p1-3: product default inputs register temporal forms so <c>Now - 1 Days</c>
/// parses without ad-hoc test-only <c>RegisterExpressionForm</c>.
/// </summary>
public sealed class TemporalPackTests {
    [Test]
    public async Task DomainInputDefaults_NowMinus1Days_ParsesAsDateOperation() {
        var expr = DslExpressionFragment.ParseExpressionFragment(
            "Now - 1 Days",
            ExtensionCatalog.Core.Language);

        await Assert.That(expr).IsTypeOf<DateOperation>();
        var dateOp = (DateOperation)expr;
        await Assert.That(dateOp.Date).IsTypeOf<Now>();
        await Assert.That(dateOp.Kind).IsEqualTo(DateOperationKind.AddDays);
        await Assert.That(dateOp.Offset).IsTypeOf<Literal>();
        await Assert.That(((Literal)dateOp.Offset).Value).IsEqualTo(-1L);
    }

    [Test]
    public async Task CreateWithTemporalPack_NowMinus1Days_ParsesAsDateOperation() {
        var inputs = ExtensionCatalog.Core.Language;

        var expr = DslExpressionFragment.ParseExpressionFragment("Now - 1 Days", inputs);

        await Assert.That(expr).IsTypeOf<DateOperation>();
        var dateOp = (DateOperation)expr;
        await Assert.That(dateOp.Date).IsTypeOf<Now>();
        await Assert.That(dateOp.Kind).IsEqualTo(DateOperationKind.AddDays);
        await Assert.That(((Literal)dateOp.Offset).Value).IsEqualTo(-1L);
    }

    [Test]
    public async Task SqlParser_NowMinus1Days_ParsesAsDateOperation() {
        var expr = DslExpressionFragment.ParseExpressionFragment(
            "Now - 1 Days",
            ExtensionCatalog.Core.Authoring);

        await Assert.That(expr).IsTypeOf<DateOperation>();
        var dateOp = (DateOperation)expr;
        await Assert.That(dateOp.Date).IsTypeOf<Now>();
        await Assert.That(dateOp.Kind).IsEqualTo(DateOperationKind.AddDays);
        await Assert.That(((Literal)dateOp.Offset).Value).IsEqualTo(-1L);
    }

    [Test]
    public async Task TemporalPack_Id_IsTemporal() {
        await Assert.That(new TemporalLibrary().Id).IsEqualTo("temporal");
    }
}