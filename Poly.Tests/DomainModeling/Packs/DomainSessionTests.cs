using Poly.DomainModeling;
using Poly.DomainModeling.Bootstrap;
using Poly.DomainModeling.Packs;
using Poly.DomainModeling.Parsing;

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
    public async Task FromInputs_WithoutTemporal_NowIsProperty() {
        var session = DomainSession.FromInputs(DomainParserInputs.Empty);
        await Assert.That(session.Language.Grammar.TryGetPattern("expr-primary", "now", out _)).IsFalse();
        var expr = DslExpressionFragment.ParseExpressionFragment("Now", session.ParserInputs);
        await Assert.That(expr).IsTypeOf<PropertyAccess>();
    }

    [Test]
    public async Task Meaning_EmptyHost_CannotLowerNow() {
        var empty = DomainHostBuilder.CreateEmpty().Build().Meaning;
        var temporal = ExtensionCatalog.Core.Language.Meaning;
        await Assert.That(empty.Lowering.TryDispatch(new Poly.DomainModeling.Packs.Temporal.Now(), _ => null!, out _))
            .IsFalse();
        await Assert.That(temporal.Lowering.TryDispatch(new Poly.DomainModeling.Packs.Temporal.Now(), _ => null!, out _))
            .IsTrue();
    }

    [Test]
    public async Task ForSource_UnknownExtension_FailClosed_Throws() {
        await Assert.That(() => DomainSession.ForSource(
                "domain D\nuses nope\n",
                ExtensionCatalog.ProductLanguage,
                failOnUnknown: true))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("nope");
    }

    [Test]
    public async Task ForSource_UnknownExtension_Default_SkipsUnknown() {
        var session = DomainSession.ForSource(
            "domain D\nuses nope\n",
            ExtensionCatalog.ProductLanguage);
        await Assert.That(session.Language.Grammar.TryGetPattern("expr-primary", "now", out _)).IsFalse();
    }

    [Test]
    public async Task Open_ProductLanguage_ParsesNow() {
        var domain = DomainFactory.Create("D") with { Extensions = [.. ExtensionCatalog.ProductLanguage] };
        var session = DomainSession.Open(domain);
        var expr = DslExpressionFragment.ParseExpressionFragment("Now", session.ParserInputs);
        await Assert.That(expr).IsTypeOf<Poly.DomainModeling.Packs.Temporal.Now>();
    }
}