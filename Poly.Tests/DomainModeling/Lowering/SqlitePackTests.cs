using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Sqlite;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// SQLite is the first shippable DBMS pack: no external service required.
/// Proves SQLite type-map defaults and explicit column-type stability.
/// </summary>
public class SqlitePackTests {
    private const string SampleDomain = """
        domain Catalog

        Item: entity {
          Name: Text
          Qty: Number
          Active: Boolean
          CreatedAt: DateTime
        }
        """;

    private static Domain ParseDomain(string poly) {
        var ctx = ExtensionCatalog.Core.Authoring;
        var changes = new PolyDslParser(poly, ctx).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException("Domain evolution failed");
        return result.Root!;
    }

    [Test]
    public async Task SqlitePack_Id_IsSqlite() {
        var pack = new SqliteLibrary();
        await Assert.That(pack.Id).IsEqualTo("sqlite");
    }

    [Test]
    public async Task AddPack_Sqlite_OverridesNumberColumnType() {
        var ctx = SessionBuilder.CreateEmpty().Load(new SqliteLibrary()).Build();
        await Assert.That(ctx.TypeMaps.ToSqlColumnType("Number")).IsEqualTo("INTEGER");
    }

    [Test]
    [Arguments("Name", "TEXT")]
    [Arguments("Qty", "INTEGER")]
    [Arguments("Active", "INTEGER")]
    [Arguments("CreatedAt", "TEXT")]
    public async Task SqliteDefaults_PropertyMapsToColumnType(string property, string sqlType) {
        var domain = ParseDomain(SampleDomain);
        var ctx = SessionBuilder.CreateEmpty().Load(new TemporalLibrary()).Load(new StorageFacetLibrary()).Load(new SqliteLibrary()).Build();
        var storage = new StorageAnalyzer(domain, typeMaps: ctx.TypeMaps, conventions: ctx.StorageConventions).Analyze();
        var col = storage.Entities.Single().Columns.Single(c => c.Name == property);
        await Assert.That(col.ColumnType).IsEqualTo(sqlType);
    }

    [Test]
    public async Task SameDomain_GenericVsSqlite_DifferentDefaults() {
        var domain = ParseDomain(SampleDomain);

        var genericCtx = ExtensionCatalog.Core.Authoring;
        var genericStorage = new StorageAnalyzer(domain, typeMaps: genericCtx.TypeMaps, conventions: genericCtx.StorageConventions).Analyze();
        var generic = genericStorage.Entities.Single().Columns
            .ToDictionary(c => c.Name, StringComparer.Ordinal);

        var sqliteCtx = SessionBuilder.CreateEmpty().Load(new TemporalLibrary()).Load(new StorageFacetLibrary()).Load(new SqliteLibrary()).Build();
        var sqliteStorage = new StorageAnalyzer(domain, typeMaps: sqliteCtx.TypeMaps, conventions: sqliteCtx.StorageConventions).Analyze();
        var sqlite = sqliteStorage.Entities.Single().Columns
            .ToDictionary(c => c.Name, StringComparer.Ordinal);

        await Assert.That(generic["Name"].ColumnType).IsEqualTo("varchar");
        await Assert.That(sqlite["Name"].ColumnType).IsEqualTo("TEXT");

        await Assert.That(generic["Qty"].ColumnType).IsEqualTo("bigint");
        await Assert.That(sqlite["Qty"].ColumnType).IsEqualTo("INTEGER");

        await Assert.That(generic["Active"].ColumnType).IsEqualTo("boolean");
        await Assert.That(sqlite["Active"].ColumnType).IsEqualTo("INTEGER");

        await Assert.That(generic["CreatedAt"].ColumnType).IsEqualTo("timestamp");
        await Assert.That(sqlite["CreatedAt"].ColumnType).IsEqualTo("TEXT");
    }

    [Test]
    public async Task ExplicitColumnType_StableUnderSqlitePack() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Code: Text column("CODE", "VARCHAR2(20)")
            }
            """);
        var ctx = SessionBuilder.CreateEmpty().Load(new TemporalLibrary()).Load(new StorageFacetLibrary()).Load(new SqliteLibrary()).Build();
        var storage = new StorageAnalyzer(domain, typeMaps: ctx.TypeMaps, conventions: ctx.StorageConventions).Analyze();
        var col = storage.Entities.Single().Columns.Single();
        await Assert.That(col.ColumnName).IsEqualTo("CODE");
        await Assert.That(col.ColumnType).IsEqualTo("VARCHAR2(20)");
    }
}