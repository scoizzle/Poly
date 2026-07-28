using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Parsing;
using Poly.Introspection;

namespace Poly.Tests.DomainModeling.Analysis;

public class DomainModelAnalyzerContextTests {
    private static Domain ParseDomain(string poly) {
        var ctx = DomainInputBuilder.CreateWithSqlPack().Build();
        var parser = new PolyDslParser(poly, ctx.Parser);
        var changes = parser.Parse();
        var result = new DomainEvolution(new Domain("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException("Domain evolution failed: " +
                string.Join("; ", result.Analysis.Diagnostics.Where(d =>
                    d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
        return result.Root!;
    }

    private static readonly Property NameProp = new("Name", new DomainTypeReference("Text"), []);
    private static readonly Property AmountProp = new("Amount", new DomainTypeReference("Number"), []);

    [Test]
    public async Task Analyze_WithDomainTree_DoesNotThrow() {
        var entity = new Entity("Widget", [NameProp], [], [], []);
        var domain = new Domain("Test", [entity], []);

        var result = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task Analyze_Incremental_WithDomainTree_DoesNotThrow() {
        var entity = new Entity("Widget", [NameProp], [], [], []);
        var domain = new Domain("Test", [entity], []);

        var priorAnalysis = DomainModelAnalyzer.Analyze(domain);

        var updatedDomain = new Domain("Test", [entity], []);
        var result = DomainModelAnalyzer.Analyze(updatedDomain, priorAnalysis, [updatedDomain]);

        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task Analyze_ProducesStorageMappingMetadata() {
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var storage = analysis.GetMetadata<StorageMappingMetadata>(domain);

        await Assert.That(storage).IsNotNull();
        await Assert.That(storage!.Storage.Entities.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Analyze_ProducesTransportMetadata() {
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var transport = analysis.GetMetadata<TransportMetadata>(domain);

        await Assert.That(transport).IsNotNull();
        await Assert.That(transport!.Transport.Entities.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Analyze_StorageMapping_IsDeterministicForSameDomainTree() {
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);

        var resultFirst = DomainModelAnalyzer.Analyze(domain);
        var resultSecond = DomainModelAnalyzer.Analyze(domain);

        var storageFirst = resultFirst.GetMetadata<StorageMappingMetadata>(domain);
        var storageSecond = resultSecond.GetMetadata<StorageMappingMetadata>(domain);

        await Assert.That(storageFirst).IsNotNull();
        await Assert.That(storageSecond).IsNotNull();

        var firstNameCol = storageFirst!.Storage.Entities[0].Columns[0];
        var secondNameCol = storageSecond!.Storage.Entities[0].Columns[0];

        await Assert.That(firstNameCol.ColumnName).IsEqualTo(secondNameCol.ColumnName);
        await Assert.That(firstNameCol.ColumnType).IsEqualTo(secondNameCol.ColumnType);
    }
}