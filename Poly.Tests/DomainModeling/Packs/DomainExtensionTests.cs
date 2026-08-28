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
        var result = new DomainEvolution(new Domain("T", [])).Apply(changes);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root!.Extensions).IsEquivalentTo(new[] { "temporal" });
    }

    [Test]
    public async Task Parse_DuplicateUses_FailsClosed() {
        await Assert.That(() => DomainCompilation.PeekExtensions("""
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
    public async Task Print_EmitsUses_InDomainOrder() {
        var domain = new Domain("T", []) { Extensions = ["temporal", "storage"] };
        var printed = new DomainDslPrinter(DomainSession.Open(domain)).Print(domain);

        await Assert.That(printed.StartsWith("domain T\nuses temporal\nuses storage\n", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task WithSeed_WhenSourceOmitsUses_StampsSdkDefaults() {
        var poly = """
            domain T
            Item: entity { Name: Text }
            """;
        var host = DomainSession.ForSource(poly, ExtensionCatalog.ProductLanguage);
        var changes = DomainCompilation.WithSeed(
            new PolyDslParser(poly, host).Parse(),
            ExtensionCatalog.ProductLanguage);
        var result = new DomainEvolution(new Domain("T", [])).Apply(changes);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root!.Extensions).IsEquivalentTo(new[] { "temporal" });
    }

    [Test]
    public async Task AddDomainExtension_Duplicate_FailsEvolution() {
        var start = new Domain("T", []) { Extensions = ["temporal"] };
        var result = new DomainEvolution(start).Apply([new AddDomainExtensionChange("temporal")]);

        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task AddDomainExtension_Temporal_SeedsTemporalPrimitives() {
        var result = new DomainEvolution(new Domain("T", [])).Apply([new AddDomainExtensionChange("temporal")]);

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
        var result = new DomainEvolution(seeded).Apply([new AddDomainExtensionChange("temporal")]);

        await Assert.That(result.Succeeded).IsTrue();
        var dates = result.Root!.Types.OfType<PrimitiveType>().Where(p => p.Name == "Date").ToList();
        await Assert.That(dates.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DomainFactory_Create_RecordsTemporal() {
        var domain = DomainFactory.Create("Orders");
        await Assert.That(domain.Extensions).Contains("temporal");
    }

    [Test]
    public async Task Parse_DateType_WithoutTemporal_UnknownType() {
        var poly = """
            domain T
            Item: entity { Due: Date }
            """;
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(new Domain("T", [])).Apply(changes);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Message.Contains("unknown type 'Date'", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Parse_DateType_WithUsesTemporal_SeedsTemporalPrimitives() {
        var poly = """
            domain T
            uses temporal
            Item: entity { Due: Date }
            """;
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(new Domain("T", [])).Apply(changes);

        await Assert.That(result.Succeeded).IsTrue();
        var names = result.Root!.Types.OfType<PrimitiveType>().Select(p => p.Name).ToHashSet();
        await Assert.That(names).Contains("Date");
        await Assert.That(names).Contains("DateTime");
        await Assert.That(names).Contains("Time");
        await Assert.That(names).Contains("Duration");
    }
}