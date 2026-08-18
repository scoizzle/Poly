using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Bootstrap;

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
    public async Task WithSeed_WhenSourceOmitsUses_PrependsSdkDefaults() {
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
    public async Task DomainFactory_Create_RecordsTemporal() {
        var domain = DomainFactory.Create("Orders");
        await Assert.That(domain.Extensions).Contains("temporal");
    }
}