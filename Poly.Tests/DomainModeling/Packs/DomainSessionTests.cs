using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

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
        await Assert.That(session.Language.Grammar.TryGetPattern("expr-primary", "now", out _)).IsTrue();
        var expr = DslExpressionFragment.ParseExpressionFragment("Now", session);
        await Assert.That(expr).IsTypeOf<PropertyAccess>();
    }

    [Test]
    public async Task ForExtensions_WithAndWithoutTemporal_SameGrammar() {
        var withTemporal = DomainSession.ForExtensions(ExtensionCatalog.ProductLanguage);
        var without = DomainSession.ForExtensions([]);
        await Assert.That(ReferenceEquals(withTemporal.Language.Grammar, DslGrammar.Core)).IsTrue();
        await Assert.That(ReferenceEquals(without.Language.Grammar, DslGrammar.Core)).IsTrue();
    }

    [Test]
    public async Task Lowering_Now_DoesNotNeedSessionMeaning() {
        var pass = new DomainExpressionLoweringPass(new LoweringContext(new Parameter("entity")));
        var lowered = pass.Lower(new Now(), new Parameter("entity"));
        await Assert.That(lowered).IsTypeOf<Member>();
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

    [Test]
    public async Task Analyze_TemporalLibrary_PublishesVocabularyBag() {
        var withTemporal = DomainSession.Open(DomainFactory.Create("D"));
        var analysis = withTemporal.Analyze(withTemporal.Domain!);
        await Assert.That(analysis.GetMetadata<TemporalVocabularyMetadata>(withTemporal.Domain!)).IsNotNull();

        var without = DomainFactory.Create("D") with { Extensions = [] };
        var empty = DomainSession.Open(without);
        var emptyAnalysis = empty.Analyze(without);
        await Assert.That(emptyAnalysis.GetMetadata<TemporalVocabularyMetadata>(without)).IsNull();
    }

    [Test]
    public async Task Emit_EntitiesOnly_DoesNotIncludeDbContext() {
        var evolved = new DomainEvolution(DomainFactory.Create("Library")).Evolve()
            .AddEntity("Book")
            .AddPropertyToEntity("Book", new Property("Title", new DomainTypeReference("Text"), []))
            .Apply();
        await Assert.That(evolved.Succeeded).IsTrue();
        var domain = evolved.Root;
        var session = DomainSession.Open(domain);
        var analysis = session.Analyze(domain);
        var files = session.Emit(domain, analysis);
        await Assert.That(files.Any(f => f.FileName == "Book.cs")).IsTrue();
        await Assert.That(files.Any(f => f.FileName.EndsWith("DbContext.cs", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Analyze_LibraryPass_PublishesBag() {
        var catalog = ExtensionCatalog.Core.With(new MarkerLibrary());
        var domain = DomainFactory.Create("D") with { Extensions = ["marker"] };
        var session = DomainSession.Open(domain, catalog);
        var analysis = session.Analyze(domain);
        await Assert.That(analysis.GetMetadata<MarkerMetadata>(domain)).IsNotNull();
    }

    [Test]
    public async Task AddAnalyzer_DuplicatePassName_Throws() {
        var builder = SessionBuilder.CreateEmpty().AddAnalyzer(new MarkerPass());
        await Assert.That(() => builder.AddAnalyzer(new MarkerPass()))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("marker-pass");
    }

    [Test]
    public async Task Analyze_LibraryPassClashesWithCorePassName_Throws() {
        var catalog = ExtensionCatalog.Core.With(new CoreClashLibrary());
        var domain = DomainFactory.Create("D") with { Extensions = ["core-clash"] };
        var session = DomainSession.Open(domain, catalog);
        await Assert.That(() => session.Analyze(domain))
            .Throws<InvalidOperationException>()
            .WithMessageContaining(StoragePass.Id);
    }

    private sealed record MarkerMetadata : IAnalysisMetadata;

    private sealed class MarkerPass : INodeAnalyzer {
        public string PassName => "marker-pass";

        public void Analyze(AnalysisContext context, Node node) {
            if (node is Domain domain)
                context.SetMetadata(domain, new MarkerMetadata());
        }
    }

    private sealed class MarkerLibrary : IDomainLibrary {
        public string Id => "marker";

        public void Register(SessionBuilder builder) =>
            builder.AddAnalyzer(new MarkerPass());
    }

    private sealed class CoreClashLibrary : IDomainLibrary {
        public string Id => "core-clash";

        public void Register(SessionBuilder builder) =>
            builder.AddAnalyzer(new StoragePass());
    }
}