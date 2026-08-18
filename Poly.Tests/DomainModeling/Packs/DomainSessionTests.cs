using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Bootstrap;

namespace Poly.Tests.DomainModeling.Packs;

public sealed class DomainSessionTests {
    [Test]
    public async Task ForSource_ParserAndPrinter_ShareGrammar() {
        var session = DomainSession.ForSource(
            "domain D\nuses temporal\n",
            ExtensionCatalog.ProductLanguage);
        var parser = new PolyDslParser("domain D\nE: entity { }\n", session);
        var printer = new DomainDslPrinter(session);
        await Assert.That(session.Language.Grammar.TryGetPattern("expr-primary", "now", out _)).IsTrue();
        _ = parser.Parse();
        _ = printer;
    }

    [Test]
    public async Task ForExtensions_WithoutTemporal_NowIsProperty() {
        var session = DomainSession.ForExtensions([]);
        await Assert.That(session.Language.Grammar.TryGetPattern("expr-primary", "now", out _)).IsFalse();
        var expr = DslExpressionFragment.ParseExpressionFragment("Now", session);
        await Assert.That(expr).IsTypeOf<PropertyAccess>();
    }

    [Test]
    public async Task Meaning_EmptyHost_CannotLowerNow() {
        var empty = SessionBuilder.CreateEmpty().Build().Meaning;
        var temporal = ExtensionCatalog.Core.Language.Meaning;
        await Assert.That(empty.Lowering.TryDispatch(new Poly.DomainModeling.Libraries.Temporal.Now(), _ => null!, out _))
            .IsFalse();
        await Assert.That(temporal.Lowering.TryDispatch(new Poly.DomainModeling.Libraries.Temporal.Now(), _ => null!, out _))
            .IsTrue();
    }

    [Test]
    public async Task ForSource_UnknownExtension_FailClosed_Throws() {
        await Assert.That(() => DomainSession.ForSource(
                "domain D\nuses nope\n",
                ExtensionCatalog.ProductLanguage))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("nope");
    }

    [Test]
    public async Task Open_UnknownExtension_Throws() {
        var domain = DomainFactory.Create("D") with { Extensions = ["nope"] };
        await Assert.That(() => DomainSession.Open(domain))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("nope");
    }

    [Test]
    public async Task Open_ProductLanguage_ParsesNow() {
        var domain = DomainFactory.Create("D") with { Extensions = [.. ExtensionCatalog.ProductLanguage] };
        var session = DomainSession.Open(domain);
        var expr = DslExpressionFragment.ParseExpressionFragment("Now", session);
        await Assert.That(expr).IsTypeOf<Poly.DomainModeling.Libraries.Temporal.Now>();
    }
}