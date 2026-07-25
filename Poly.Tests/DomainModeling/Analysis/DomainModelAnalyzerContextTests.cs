using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Parsing;
using Poly.Introspection;
using Poly.Packs.Sqlite;

namespace Poly.Tests.DomainModeling.Analysis;

public class DomainModelAnalyzerContextTests {
    private static Domain ParseDomain(string poly) {
        var ctx = DomainAuthoringContext.CreateWithSqlPack();
        var parser = new PolyDslParser(poly, ctx);
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
    public async Task Analyze_WithNullAuthoring_MatchesParameterless() {
        var entity = new Entity("Widget", [NameProp], [], [], []);
        var domain = new Domain("Test", [entity], []);

        var resultDefault = DomainModelAnalyzer.Analyze(domain);
        var resultWithContext = DomainModelAnalyzer.Analyze(domain, authoring: null);

        await Assert.That(resultWithContext).IsNotNull();
        await Assert.That(resultWithContext.Diagnostics.Count)
            .IsEqualTo(resultDefault.Diagnostics.Count);
    }

    [Test]
    public async Task Analyze_WithAuthoringContext_DoesNotThrow() {
        var entity = new Entity("Widget", [NameProp], [], [], []);
        var domain = new Domain("Test", [entity], []);
        var authoring = DomainAuthoringContext.CreateWithSqlPack();

        var result = DomainModelAnalyzer.Analyze(domain, authoring);

        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task Analyze_WithAuthoringContext_Incremental_DoesNotThrow() {
        var entity = new Entity("Widget", [NameProp], [], [], []);
        var domain = new Domain("Test", [entity], []);

        var priorAnalysis = DomainModelAnalyzer.Analyze(domain);
        var authoring = DomainAuthoringContext.CreateWithSqlPack();

        var updatedDomain = new Domain("Test", [entity], []);
        var result = DomainModelAnalyzer.Analyze(updatedDomain, authoring, priorAnalysis, [updatedDomain]);

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
    public async Task Analyze_WithDifferentTypeMaps_ProducesDifferentColumnTypes() {
        // D3.6: Real pack-variance via DomainModelAnalyzer.Analyze(domain, authoring).
        // Prove that different TypeMappingRegistry configurations produce different
        // storage column types in the AnalysisResult.
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);

        var generic = DomainAuthoringContext.CreateWithSqlPack();
        var sqlite = DomainAuthoringContext.CreateWithSqlPack().AddSqliteDefaults();

        var resultGeneric = DomainModelAnalyzer.Analyze(domain, generic);
        var resultSqlite = DomainModelAnalyzer.Analyze(domain, sqlite);

        var storageGeneric = resultGeneric.GetMetadata<StorageMappingMetadata>(domain);
        var storageSqlite = resultSqlite.GetMetadata<StorageMappingMetadata>(domain);

        await Assert.That(storageGeneric).IsNotNull();
        await Assert.That(storageSqlite).IsNotNull();

        // Same logical entity, same property — different type map → different column type
        var genericNameCol = storageGeneric!.Storage.Entities[0].Columns[0];
        var sqliteNameCol = storageSqlite!.Storage.Entities[0].Columns[0];

        await Assert.That(genericNameCol.ColumnName).IsEqualTo(sqliteNameCol.ColumnName);
        await Assert.That(genericNameCol.ColumnType).IsNotEqualTo(sqliteNameCol.ColumnType);
        await Assert.That(genericNameCol.ColumnType).IsEqualTo("varchar");
        await Assert.That(sqliteNameCol.ColumnType).IsEqualTo("TEXT");
    }
}