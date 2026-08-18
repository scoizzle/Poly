using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Packs.SqlServer;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Tests for <see cref="SqlServerDefaults"/> — verifies that the same domain
/// produces different storage defaults when composed with different packs.
/// Also tests the identifier-length convention.
/// </summary>
public class SqlServerPackTests {
    private static Domain ParseDomain(string poly) {
        var ctx = ExtensionCatalog.Core.Authoring;
        var parser = new PolyDslParser(poly, ctx);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException("Domain evolution failed: " +
                string.Join("; ", result.Analysis.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return result.Root!;
    }

    private static StorageModel AnalyzeStorage(
        Domain domain,
        DomainSession? authoring = null) {
        var ctx = authoring ?? ExtensionCatalog.Core.Authoring;
        return new StorageAnalyzer(domain, typeMaps: ctx.TypeMaps, conventions: ctx.StorageConventions).Analyze();
    }

    #region Type-map overrides

    [Test]
    public async Task AddPack_SqlServer_AppliesTypeMaps() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Title: Text
              CreatedAt: DateTime
              IsActive: Boolean
            }
            """);
        var ctx = SessionBuilder.CreateEmpty().Load(new TemporalLibrary()).Load(new StorageFacetLibrary())
            .Load(new SqlServerLibrary())
            .Build();
        var storage = AnalyzeStorage(domain, ctx);
        var cols = storage.Entities.Single().Columns
            .ToDictionary(c => c.Name, StringComparer.Ordinal);
        await Assert.That(cols["Title"].ColumnType).IsEqualTo("nvarchar(max)");
        await Assert.That(cols["CreatedAt"].ColumnType).IsEqualTo("datetime2");
        await Assert.That(cols["IsActive"].ColumnType).IsEqualTo("bit");
    }

    [Test]
    public async Task AddPack_SqlServer_AppliesIdentifierConvention() {
        var longName = new string('A', 129);
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);
        var item = domain.Types.OfType<Entity>().Single();
        var prop = item.Properties.Single() with {
            Facets = [
                new Annotation("column", new Dictionary<string, AnnotationValue> {
                    ["0"] = new AnnotationString(longName),
                })
            ]
        };
        var faceted = domain with { Types = [item with { Properties = [prop] }] };

        var ctx = SessionBuilder.CreateEmpty().Load(new TemporalLibrary()).Load(new StorageFacetLibrary())
            .Load(new SqlServerLibrary())
            .Build();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AnalyzeStorage(faceted, ctx));
        await Assert.That(ex!.Message).Contains("exceeds SQL Server maximum");
    }

    [Test]
    public async Task SqlServerDefaults_TextMapsToNvarcharMax() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Title: Text
              Description: Text
            }
            """);
        var ctx = SessionBuilder.CreateEmpty().Load(new TemporalLibrary()).Load(new StorageFacetLibrary())
            .Load(new SqlServerLibrary())
            .Build();
        var storage = AnalyzeStorage(domain, ctx);
        var cols = storage.Entities.Single().Columns;
        await Assert.That(cols.All(c => c.ColumnType == "nvarchar(max)")).IsTrue();
    }

    [Test]
    public async Task SqlServerDefaults_BooleanMapsToBit() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              IsActive: Boolean
              Flag: Boolean
            }
            """);
        var ctx = SessionBuilder.CreateEmpty().Load(new TemporalLibrary()).Load(new StorageFacetLibrary())
            .Load(new SqlServerLibrary())
            .Build();
        var storage = AnalyzeStorage(domain, ctx);
        var cols = storage.Entities.Single().Columns;
        await Assert.That(cols.All(c => c.ColumnType == "bit")).IsTrue();
    }

    [Test]
    public async Task SqlServerDefaults_DateTimeMapsToDatetime2() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              CreatedAt: DateTime
              UpdatedAt: DateTime
            }
            """);
        var ctx = SessionBuilder.CreateEmpty().Load(new TemporalLibrary()).Load(new StorageFacetLibrary())
            .Load(new SqlServerLibrary())
            .Build();
        var storage = AnalyzeStorage(domain, ctx);
        var cols = storage.Entities.Single().Columns;
        await Assert.That(cols.All(c => c.ColumnType == "datetime2")).IsTrue();
    }

    [Test]
    public async Task SqlServerDefaults_GuidMapsToUniqueidentifier() {
        // Guid is not a DSL primitive keyword — test the registry directly
        var registry = new TypeMappingRegistry();
        SqlServerDefaults.ApplyTypeMaps(registry);
        await Assert.That(registry.ToSqlColumnType("Guid")).IsEqualTo("uniqueidentifier");
        await Assert.That(registry.ToSqlColumnType("Uuid")).IsEqualTo("uniqueidentifier");
    }

    [Test]
    public async Task SqlServerDefaults_Int32MapsToInt() {
        // Int32 is not a DSL primitive keyword — test the registry directly
        var registry = new TypeMappingRegistry();
        SqlServerDefaults.ApplyTypeMaps(registry);
        await Assert.That(registry.ToSqlColumnType("Int32")).IsEqualTo("int");
    }

    [Test]
    public async Task SameDomain_DifferentPacks_DifferentDefaults() {
        // Proves P4.3: same domain, two pack sets → different defaults
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Name: Text
              CreatedAt: DateTime
              IsActive: Boolean
            }
            """);

        // Generic SQL defaults (no SqlServer pack)
        var genericCtx = ExtensionCatalog.Core.Authoring;
        var genericInfra = AnalyzeStorage(domain, genericCtx);
        var genericCols = genericInfra.Entities.Single().Columns
            .ToDictionary(c => c.Name, StringComparer.Ordinal);

        // SQL Server defaults
        var ssCtx = SessionBuilder.CreateEmpty().Load(new TemporalLibrary()).Load(new StorageFacetLibrary())
            .Load(new SqlServerLibrary())
            .Build();
        var ssInfra = AnalyzeStorage(domain, ssCtx);
        var ssCols = ssInfra.Entities.Single().Columns
            .ToDictionary(c => c.Name, StringComparer.Ordinal);

        // Same domain, different type maps
        await Assert.That(genericCols["Name"].ColumnType).IsEqualTo("varchar");
        await Assert.That(ssCols["Name"].ColumnType).IsEqualTo("nvarchar(max)");

        await Assert.That(genericCols["CreatedAt"].ColumnType).IsEqualTo("timestamp");
        await Assert.That(ssCols["CreatedAt"].ColumnType).IsEqualTo("datetime2");

        await Assert.That(genericCols["IsActive"].ColumnType).IsEqualTo("boolean");
        await Assert.That(ssCols["IsActive"].ColumnType).IsEqualTo("bit");
    }

    #endregion

    #region Explicit annotation stability

    [Test]
    public async Task ExplicitColumnTypeOverride_StableAcrossPacks() {
        // P4.3: explicit column type arg is preserved regardless of pack defaults
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Code: Text column("CODE", "VARCHAR2(20)")
            }
            """);

        var genericCtx = ExtensionCatalog.Core.Authoring;
        var genericInfra = AnalyzeStorage(domain, genericCtx);
        var genericCol = genericInfra.Entities.Single().Columns.Single();
        await Assert.That(genericCol.ColumnType).IsEqualTo("VARCHAR2(20)");

        var ssCtx = SessionBuilder.CreateEmpty().Load(new TemporalLibrary()).Load(new StorageFacetLibrary())
            .Load(new SqlServerLibrary())
            .Build();
        var ssInfra = AnalyzeStorage(domain, ssCtx);
        var ssCol = ssInfra.Entities.Single().Columns.Single();
        await Assert.That(ssCol.ColumnType).IsEqualTo("VARCHAR2(20)");
    }

    #endregion

    #region Identifier-length convention

    [Test]
    public async Task OversizedColumnName_FailsClosed() {
        // Name that exceeds 128 chars
        var longName = new string('A', 129);
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);
        var item = domain.Types.OfType<Entity>().Single();
        var prop = item.Properties.Single() with {
            Facets = [
                new Annotation("column", new Dictionary<string, AnnotationValue> {
                    ["0"] = new AnnotationString(longName),
                })
            ]
        };
        var faceted = domain with { Types = [item with { Properties = [prop] }] };

        var ctx = SessionBuilder.CreateEmpty().Load(new TemporalLibrary()).Load(new StorageFacetLibrary())
            .Load(new SqlServerLibrary())
            .Build();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AnalyzeStorage(faceted, ctx));
        await Assert.That(ex!.Message).Contains("exceeds SQL Server maximum");
    }

    [Test]
    public async Task AcceptableColumnName_Passes() {
        // 128 chars should be fine
        var longName = new string('A', 128);
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);
        var item = domain.Types.OfType<Entity>().Single();
        var prop = item.Properties.Single() with {
            Facets = [
                new Annotation("column", new Dictionary<string, AnnotationValue> {
                    ["0"] = new AnnotationString(longName),
                })
            ]
        };
        var faceted = domain with { Types = [item with { Properties = [prop] }] };

        var ctx = SessionBuilder.CreateEmpty().Load(new TemporalLibrary()).Load(new StorageFacetLibrary())
            .Load(new SqlServerLibrary())
            .Build();
        var infra = AnalyzeStorage(faceted, ctx);
        await Assert.That(infra.Entities.Single().Columns.Single().ColumnName)
            .IsEqualTo(longName);
    }

    [Test]
    public async Task OversizedTableName_FailsClosed() {
        var longName = new string('B', 129);
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);
        var item = domain.Types.OfType<Entity>().Single() with {
            Facets = [
                new Annotation("table", new Dictionary<string, AnnotationValue> {
                    ["0"] = new AnnotationString(longName),
                })
            ]
        };
        var faceted = domain with { Types = [item with { Facets = item.Facets }] };

        var ctx = SessionBuilder.CreateEmpty().Load(new TemporalLibrary()).Load(new StorageFacetLibrary())
            .Load(new SqlServerLibrary())
            .Build();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AnalyzeStorage(faceted, ctx));
        await Assert.That(ex!.Message).Contains("exceeds SQL Server maximum");
    }

    #endregion

    #region Composition with convention chain

    [Test]
    public async Task SqlServerDefaults_WithPrefixConvention_AppliesBoth() {
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);

        var ctx = SessionBuilder.CreateEmpty().Load(new TemporalLibrary()).Load(new StorageFacetLibrary())
            .Load(new SqlServerLibrary())
            .AddStorageConvention(new PrefixConvention("tbl_"))
            .Build();

        var infra = AnalyzeStorage(domain, ctx);
        var col = infra.Entities.Single().Columns.Single();
        await Assert.That(col.ColumnName).IsEqualTo("tbl_name");
        await Assert.That(col.ColumnType).IsEqualTo("nvarchar(max)");
    }

    #endregion
}

/// <summary>
/// Test-only storage convention that prepends a prefix to column names.
/// Verifies that SqlServerDefaults' type-map overrides survive convention chaining.
/// </summary>
file sealed class PrefixConvention : IStorageConvention {
    private readonly string _prefix;

    public PrefixConvention(string prefix) {
        _prefix = prefix;
    }

    public StorageEntity? ProjectEntity(Entity entity, StorageEntity baseline) => null;

    public StorageColumn? ProjectColumn(Property property, StorageColumn baseline) {
        // Construct a new instance with the overridden column name
        return new StorageColumn(
            baseline.Source,
            baseline.ColumnType,
            baseline.ClrTypeName,
            baseline.IsEnum,
            baseline.IsRequired,
            baseline.HasDefault,
            baseline.IsUnique,
            baseline.MaxLength,
            columnName: _prefix + baseline.ColumnName);
    }
}