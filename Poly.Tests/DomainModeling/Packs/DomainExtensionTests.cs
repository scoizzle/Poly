using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Packs;

public sealed class DomainExtensionTests {
    [Test]
    public async Task Parse_UsesTemporal_StampsExtensionId() {
        var poly = """
            domain T
            uses temporal
            Item: entity { Name: Text }
            """;
        var host = DomainSession.ForSource(poly, ExtensionCatalog.ProductLanguage);
        var changes = new PolyDslParser(poly, host).Parse();
        var result = new DomainEvolution(new Domain("T", [])).ApplyAndAnalyze(changes);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root!.Extensions).IsEquivalentTo(new[] { "temporal" });
    }

    [Test]
    public async Task Parse_DuplicateUses_FailsClosed() {
        await Assert.That(() => DslSyntaxTree.Parse("""
            domain T
            uses temporal
            uses temporal
            """))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Parse_UnknownExtension_ResolveHostThrows() {
        await Assert.That(() => DomainSession.ForExtensions(["nope"]))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("nope");
    }

    [Test]
    public async Task Print_OmitsImplicitTemporal() {
        var domain = new Domain("T", []) { Extensions = ["temporal", "storage"] };
        var printed = new DomainDslPrinter(DomainSession.Open(domain)).Print(domain);

        await Assert.That(printed.StartsWith("domain T\nuses storage\n", StringComparison.Ordinal)).IsTrue();
        await Assert.That(printed.Contains("uses temporal", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task WithSeed_WhenSourceOmitsUses_RecordsStorageNotTemporal() {
        var poly = """
            domain T
            Item: entity { Name: Text }
            """;
        var host = DomainSession.ForSource(poly, ExtensionCatalog.ProductAuthoring);
        var changes = DomainCompilation.WithSeed(
            new PolyDslParser(poly, host).Parse(),
            ExtensionCatalog.ProductAuthoring);
        var result = new DomainEvolution(new Domain("T", [])).ApplyAndAnalyze(changes);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root!.Extensions).IsEquivalentTo(new[] { "storage" });
        await Assert.That(result.Root.Extensions.Contains("temporal")).IsFalse();
    }

    [Test]
    public async Task AddDomainExtension_Duplicate_FailsEvolution() {
        var start = new Domain("T", []) { Extensions = ["temporal"] };
        var result = new DomainEvolution(start).ApplyAndAnalyze([new AddDomainExtensionChange("temporal")]);

        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task AddDomainExtension_SessionHasNotLoadedId_Throws() {
        var session = DomainSession.ForExtensions(["temporal"]);
        var start = new Domain("T", []) { Extensions = ["temporal"] };
        await Assert.That(() => new DomainEvolution(start).ApplyAndAnalyze(
                [new AddDomainExtensionChange("storage")], session: session))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("storage");
    }

    [Test]
    public async Task GetOrAnalyze_UnboundUnknownExtension_Throws() {
        var domain = new Domain("T", []) { Extensions = ["sqlite"] };
        await Assert.That(() => RuntimeAnalysisCache.GetOrAnalyze(domain))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("sqlite");
    }

    [Test]
    public async Task Policy_SameNameOnEntityAndStage_IsRedefinition() {
        var session = DomainSession.ForExtensions(ExtensionCatalog.ProductLanguage);
        var result = new DomainEvolution(new Domain("T", [])).Evolve()
            .AddEntity("Item")
            .AddPolicyToEntity("Item", "Ready", DomainExpression.Literal(true))
            .AddStage("Item", "Open")
            .AddPolicyToStage("Item", "Open", "Ready", DomainExpression.Literal(false))
            .Apply(session: session);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Message.Contains("already defined", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task AddDomainExtension_Temporal_SeedsTemporalPrimitives() {
        var result = new DomainEvolution(new Domain("T", [])).ApplyAndAnalyze([new AddDomainExtensionChange("temporal")]);

        await Assert.That(result.Succeeded).IsTrue();
        var names = result.Root!.Types.OfType<PrimitiveType>().Select(p => p.Name).ToHashSet();
        await Assert.That(names).Contains("Date");
        await Assert.That(names).Contains("Time");
        await Assert.That(names).Contains("DateTime");
        await Assert.That(names).Contains("Duration");
    }

    [Test]
    public async Task AddDomainExtension_Temporal_AfterCanonicalBuiltins_SeedsOneDate() {
        var seeded = CanonicalBuiltInTypeCatalog.ApplyTo(new Domain("T", []));
        var result = new DomainEvolution(seeded).ApplyAndAnalyze([new AddDomainExtensionChange("temporal")]);

        await Assert.That(result.Succeeded).IsTrue();
        var dates = result.Root!.Types.OfType<PrimitiveType>().Where(p => p.Name == "Date").ToList();
        await Assert.That(dates.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DomainFactory_Create_DoesNotRecordImplicitTemporal() {
        var domain = DomainFactory.Create("Orders");
        await Assert.That(domain.Extensions).IsEmpty();
        await Assert.That(domain.Types.OfType<PrimitiveType>().Any(p => p.Name == "Date")).IsTrue();
    }

    [Test]
    public async Task Parse_DateType_WithoutTemporalInRawSession_UnknownType() {
        var poly = """
            domain T
            Item: entity { Due: Date }
            """;
        var raw = SessionBuilder.CreateEmpty().LoadCore().Build();
        await Assert.That(() => new PolyDslParser(poly, raw).Parse())
            .Throws<FormatException>()
            .WithMessageContaining("Date");
    }

    [Test]
    public async Task Parse_DateType_WithoutUses_SeedsTemporal() {
        var poly = """
            domain T
            Item: entity { Due: Date }
            """;
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(new Domain("T", [])).ApplyAndAnalyze(changes);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root!.Extensions.Contains("temporal")).IsFalse();
        await Assert.That(result.Root.Types.OfType<PrimitiveType>().Any(p => p.Name == "Date")).IsTrue();
    }

    [Test]
    public async Task Parse_DateType_WithUsesTemporal_SeedsTemporalPrimitives() {
        var poly = """
            domain T
            uses temporal
            Item: entity { Due: Date }
            """;
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(new Domain("T", [])).ApplyAndAnalyze(changes);

        await Assert.That(result.Succeeded).IsTrue();
        var names = result.Root!.Types.OfType<PrimitiveType>().Select(p => p.Name).ToHashSet();
        await Assert.That(names).Contains("Date");
        await Assert.That(names).Contains("DateTime");
        await Assert.That(names).Contains("Time");
        await Assert.That(names).Contains("Duration");
    }
}