using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Sqlite;

using CompileMode = Poly.DslCompiler.CompileMode;
using Compiler = Poly.DslCompiler.DslCompiler;
using DbmsPack = Poly.DslCompiler.DbmsPack;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// SQLite is the first shippable DBMS pack: no external service required.
/// Proves type-map defaults and real host composition via <see cref="Compiler"/>.
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
    public async Task SqliteDefaults_TextMapsToText() {
        var domain = ParseDomain(SampleDomain);
        var ctx = SessionBuilder.CreateEmpty().Load(new TemporalLibrary()).Load(new StorageFacetLibrary()).Load(new SqliteLibrary()).Build();
        var storage = new StorageAnalyzer(domain, typeMaps: ctx.TypeMaps, conventions: ctx.StorageConventions).Analyze();
        var name = storage.Entities.Single().Columns.Single(c => c.Name == "Name");
        await Assert.That(name.ColumnType).IsEqualTo("TEXT");
    }

    [Test]
    public async Task SqliteDefaults_NumberMapsToInteger() {
        var domain = ParseDomain(SampleDomain);
        var ctx = SessionBuilder.CreateEmpty().Load(new TemporalLibrary()).Load(new StorageFacetLibrary()).Load(new SqliteLibrary()).Build();
        var storage = new StorageAnalyzer(domain, typeMaps: ctx.TypeMaps, conventions: ctx.StorageConventions).Analyze();
        var qty = storage.Entities.Single().Columns.Single(c => c.Name == "Qty");
        await Assert.That(qty.ColumnType).IsEqualTo("INTEGER");
    }

    [Test]
    public async Task SqliteDefaults_BooleanMapsToInteger() {
        var domain = ParseDomain(SampleDomain);
        var ctx = SessionBuilder.CreateEmpty().Load(new TemporalLibrary()).Load(new StorageFacetLibrary()).Load(new SqliteLibrary()).Build();
        var storage = new StorageAnalyzer(domain, typeMaps: ctx.TypeMaps, conventions: ctx.StorageConventions).Analyze();
        var active = storage.Entities.Single().Columns.Single(c => c.Name == "Active");
        await Assert.That(active.ColumnType).IsEqualTo("INTEGER");
    }

    [Test]
    public async Task SqliteDefaults_DateTimeMapsToText() {
        var domain = ParseDomain(SampleDomain);
        var ctx = SessionBuilder.CreateEmpty().Load(new TemporalLibrary()).Load(new StorageFacetLibrary()).Load(new SqliteLibrary()).Build();
        var storage = new StorageAnalyzer(domain, typeMaps: ctx.TypeMaps, conventions: ctx.StorageConventions).Analyze();
        var createdAt = storage.Entities.Single().Columns.Single(c => c.Name == "CreatedAt");
        await Assert.That(createdAt.ColumnType).IsEqualTo("TEXT");
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

    [Test]
    public async Task DslCompiler_EntitiesMode_EmitsEntityTypesFromProjection() {
        // entity emit uses DomainProgramProjection.ToSyntax on finished AnalysisResult
        // (no mid-pipeline EntitySyntaxMetadata soft-skip).
        var compiler = new Compiler();
        var result = compiler.Compile(SampleDomain, CompileMode.Entities);
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Files).IsNotNull();

        var fileNames = result.Files!.Select(f => f.FileName).ToList();
        await Assert.That(fileNames).Contains("Item.cs");

        var item = result.Files!.Single(f => f.FileName == "Item.cs").Source;
        await Assert.That(item).Contains("Item");
        await Assert.That(item).Contains("Name");
        await Assert.That(item).Contains("Qty");
    }

    [Test]
    public async Task DslCompiler_SqlitePack_EmitsSqliteColumnTypesInDbContext() {
        // Host composition proof: compiler + --dbms sqlite path is not test-only.
        var compiler = new Compiler();
        var result = compiler.Compile(SampleDomain, CompileMode.Db, DbmsPack.Sqlite);
        await Assert.That(result.Success).IsTrue();

        var db = result.Files!.Single(f => f.FileName.EndsWith("DbContext.cs", StringComparison.Ordinal)).Source;
        await Assert.That(db).Contains(".HasColumnType(\"TEXT\")");
        await Assert.That(db).Contains(".HasColumnType(\"INTEGER\")");
        await Assert.That(db).DoesNotContain(".HasColumnType(\"varchar\")");
        await Assert.That(db).DoesNotContain(".HasColumnType(\"boolean\")");
        await Assert.That(db).DoesNotContain(".HasColumnType(\"timestamp\")");

        // Entity files still emit under Db mode (export-time projection).
        await Assert.That(result.Files!.Select(f => f.FileName)).Contains("Item.cs");
    }

    [Test]
    public async Task DslCompiler_GenericPack_EmitsGenericColumnTypesInDbContext() {
        var compiler = new Compiler();
        var result = compiler.Compile(SampleDomain, CompileMode.Db, DbmsPack.Generic);
        await Assert.That(result.Success).IsTrue();

        var db = result.Files!.Single(f => f.FileName.EndsWith("DbContext.cs", StringComparison.Ordinal)).Source;
        await Assert.That(db).Contains(".HasColumnType(\"varchar\")");
        await Assert.That(db).Contains(".HasColumnType(\"bigint\")");
        await Assert.That(db).Contains(".HasColumnType(\"boolean\")");
        await Assert.That(db).Contains(".HasColumnType(\"timestamp\")");
    }

    [Test]
    public async Task DslCompiler_SameDomain_SqliteVsGeneric_DbContextDiffers() {
        var compiler = new Compiler();
        var generic = compiler.Compile(SampleDomain, CompileMode.Db, DbmsPack.Generic);
        var sqlite = compiler.Compile(SampleDomain, CompileMode.Db, DbmsPack.Sqlite);
        await Assert.That(generic.Success).IsTrue();
        await Assert.That(sqlite.Success).IsTrue();

        var genericDb = generic.Files!.Single(f => f.FileName.EndsWith("DbContext.cs", StringComparison.Ordinal)).Source;
        var sqliteDb = sqlite.Files!.Single(f => f.FileName.EndsWith("DbContext.cs", StringComparison.Ordinal)).Source;

        await Assert.That(genericDb).IsNotEqualTo(sqliteDb);
        await Assert.That(sqliteDb).Contains("HasColumnType(\"TEXT\")");
        await Assert.That(genericDb).Contains("HasColumnType(\"varchar\")");
    }

    [Test]
    public async Task DslCompiler_CompileWithSqlitePack_MatchesDbmsSqliteColumnTypes() {
        // pack-2-5: the pack model is primary — compiling with `new SqliteLibrary()`
        // must produce the same DbContext column types as `--dbms sqlite` today.
        var compiler = new Compiler();
        var viaEnum = compiler.Compile(SampleDomain, CompileMode.Db, DbmsPack.Sqlite);
        var viaPack = compiler.Compile(SampleDomain, CompileMode.Db, new SqliteLibrary());
        await Assert.That(viaEnum.Success).IsTrue();
        await Assert.That(viaPack.Success).IsTrue();

        var enumDb = viaEnum.Files!.Single(f => f.FileName.EndsWith("DbContext.cs", StringComparison.Ordinal)).Source;
        var packDb = viaPack.Files!.Single(f => f.FileName.EndsWith("DbContext.cs", StringComparison.Ordinal)).Source;
        await Assert.That(packDb).IsEqualTo(enumDb);
        await Assert.That(packDb).Contains(".HasColumnType(\"TEXT\")");
        await Assert.That(packDb).Contains(".HasColumnType(\"INTEGER\")");
    }

    [Test]
    public async Task ParseDbmsPack_AcceptsAliases_FailsUnknown() {
        await Assert.That(Compiler.ParseDbmsPack("sqlite")).IsEqualTo(DbmsPack.Sqlite);
        await Assert.That(Compiler.ParseDbmsPack("sqlite3")).IsEqualTo(DbmsPack.Sqlite);
        await Assert.That(Compiler.ParseDbmsPack("generic")).IsEqualTo(DbmsPack.Generic);
        await Assert.That(Compiler.ParseDbmsPack("sqlserver")).IsEqualTo(DbmsPack.SqlServer);

        var ex = Assert.Throws<FormatException>(() => Compiler.ParseDbmsPack("oracle"));
        await Assert.That(ex!.Message).Contains("Unknown DBMS pack");
    }

    [Test]
    public async Task ForExtensions_Sqlite_RegistersTypeMaps() {
        var catalog = ExtensionCatalog.Core.With(new SqliteLibrary());
        var session = DomainSession.ForExtensions(
            [ExtensionCatalog.TemporalId, ExtensionCatalog.StorageFacetsId, "sqlite"],
            catalog);
        await Assert.That(session.TypeMaps.ToSqlColumnType("Text")).IsEqualTo("TEXT");
        await Assert.That(session.TypeMaps.ToSqlColumnType("Boolean")).IsEqualTo("INTEGER");
        await Assert.That(session.Annotations.CanAccept("column")).IsTrue();
        await Assert.That(session.Annotations.CanAccept("table")).IsTrue();
    }

    [Test]
    public async Task Compile_LoadSqlite_SameSessionMapsLandInDbContext() {
        var result = new Compiler()
            .Load(new SqliteLibrary())
            .Compile(SampleDomain, CompileMode.Db);
        await Assert.That(result.Success).IsTrue();
        var db = result.Files!.Single(f => f.FileName.EndsWith("DbContext.cs", StringComparison.Ordinal)).Source;
        await Assert.That(db).Contains(".HasColumnType(\"TEXT\")");
        await Assert.That(db).Contains(".HasColumnType(\"INTEGER\")");
    }

    [Test]
    public async Task DslCompiler_AllMode_EmitsDbContextAndProgramViaIr() {
        // G6.R.1: CompileMode.All production IR wire-up smoke.
        // Verifies both DbContext and Program.cs emit through the
        // GenerateCompilationUnit + CSharpGenerator production path.
        var compiler = new Compiler();
        var result = compiler.Compile(SampleDomain, CompileMode.All, DbmsPack.Sqlite);
        await Assert.That(result.Success).IsTrue();

        // Must have entity files + DbContext + Program.cs + demo.http
        var fileNames = result.Files!.Select(f => f.FileName).ToList();

        // DbContext file is domain-named (not hardcoded "LibraryDbContext.cs")
        await Assert.That(fileNames).Contains("CatalogDbContext.cs");
        await Assert.That(fileNames).Contains("Program.cs");
        await Assert.That(fileNames).Contains("demo.http");

        // Structural markers: IR-backed DbContext
        var files = result.Files!;
        var dbFile = files.Single(f => f.FileName == "CatalogDbContext.cs");
        await Assert.That(dbFile.Source).Contains("class CatalogDbContext : DbContext");
        await Assert.That(dbFile.Source).Contains("DbSet<Item> Items");
        await Assert.That(dbFile.Source).Contains("OnModelCreating(ModelBuilder modelBuilder)");

        // Structural markers: IR-backed Program.cs
        var progFile = files.Single(f => f.FileName == "Program.cs");
        await Assert.That(progFile.Source).Contains("WebApplication.CreateBuilder(args)");
        await Assert.That(progFile.Source).Contains("MapGet");
        await Assert.That(progFile.Source).Contains("MapPost");
    }

    [Test]
    public async Task DslCompiler_EmitsScaffoldingFile_WithEnumsAndDomainResult() {
        // The entity files reference enums + DomainResult<T> — the compiler must
        // emit a scaffolding file or the output does not compile standalone.
        var compiler = new Compiler();
        var result = compiler.Compile("""
            domain Demo
            Color: enum { Red, Green }
            Item: entity { Name: Text Color: Color }
            """, CompileMode.Entities);
        await Assert.That(result.Success).IsTrue();

        var scaffolding = result.Files!.Single(f => f.FileName == "Poly.Types.cs").Source;
        await Assert.That(scaffolding).Contains("enum Color");
        await Assert.That(scaffolding).Contains("record DomainResult");
        await Assert.That(scaffolding).Contains("record DomainResult<T>");

        // Entity file references them without defining them.
        var item = result.Files!.Single(f => f.FileName == "Item.cs").Source;
        await Assert.That(item).Contains("DomainResult<Item>");
    }

    [Test]
    public async Task DslCompiler_AllMode_Sqlite_EmitsUseSqliteAndEnsureCreated() {
        // --mode all --dbms sqlite must emit UseSqlite + EnsureCreatedAsync
        // (matches the shipped demo Program.cs), not the generic InMemory fallback.
        var compiler = new Compiler();
        var sqlite = compiler.Compile(SampleDomain, CompileMode.All, DbmsPack.Sqlite);
        await Assert.That(sqlite.Success).IsTrue();
        var sqliteProg = sqlite.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(sqliteProg).Contains("UseSqlite");
        await Assert.That(sqliteProg).Contains("EnsureCreatedAsync");
        await Assert.That(sqliteProg).DoesNotContain("UseInMemoryDatabase");

        var generic = compiler.Compile(SampleDomain, CompileMode.All, DbmsPack.Generic);
        await Assert.That(generic.Success).IsTrue();
        var genericProg = generic.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(genericProg).Contains("UseInMemoryDatabase");
        await Assert.That(genericProg).DoesNotContain("UseSqlite");
    }

    [Test]
    public async Task DslCompiler_AllMode_EntityWithCollections_EmitsEmptyCollectionArgs() {
        // The POST endpoint + seed code construct root entities via Entity.Create(...)
        // — collection navs are ctor params (IEnumerable<T>) but omitted from ESM;
        // the generators must append Enumerable.Empty<T>() or the call is CS7036.
        var compiler = new Compiler();
        var result = compiler.Compile("""
            domain Demo
            Token: entity { Kind: Text }
            Box: entity {
              Name: Text
              tokens: many Token
            }
            """, CompileMode.All, DbmsPack.Generic);
        await Assert.That(result.Success).IsTrue();

        var prog = result.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(prog).Contains("Box.Create(");
        await Assert.That(prog).Contains("Enumerable.Empty<Token>()");
    }

    [Test]
    public async Task DslCompiler_Dto_IncludesEnumTypedScalarFromConstructorMetadata() {
        // The DTO mirrors the entity's CREATE signature (from ESM.ConstructorParameters).
        // A scalar enum-typed prop (Genre) is part of that signature — the old
        // `_entities.Any(...)` filter would have EXCLUDED it (Genre is not an entity).
        var compiler = new Compiler();
        var result = compiler.Compile("""
            domain Demo
            Genre: enum { Fiction, NonFiction }
            Book: entity {
              Title: Text
              Genre: Genre
            }
            """, CompileMode.All, DbmsPack.Generic);
        await Assert.That(result.Success).IsTrue();

        var prog = result.Files!.Single(f => f.FileName == "Program.cs").Source;
        // Enum type maps to its own CLR name (Genre) with the allowed-value union declared
        // via [EnumDataType] — not excluded by entity-ness.
        await Assert.That(prog).Contains("public record BookDto\n{\n    [EnumDataType(typeof(Genre))]\n    public Genre Genre { get; init; }");
        await Assert.That(prog).Contains("public string Title { get; init; } = default!;");
        // POST endpoint passes the DTO members into Book.Create in the same order.
        await Assert.That(prog).Contains("Book.Create(dto.Genre, dto.Title)");
    }

    [Test]
    public async Task DslCompiler_ActionDto_EmitsImplicitRangeFromAssignTarget() {
        // Transport: an action parameter that flows directly into a range-constrained
        // property (assign Stock to amount) carries an IMPLICIT [Range] on its action DTO —
        // not declared in the DSL, but proven by the action's own effects. The endpoint
        // enforces the target's envelope at the API boundary.
        var compiler = new Compiler();
        var result = compiler.Compile("""
            domain Demo
            Book: entity {
              Stock: Number range(0, 1000)

              Restock: action (amount: Number) {
                assign Stock to amount
              }
            }
            """, CompileMode.All, DbmsPack.Generic);
        await Assert.That(result.Success).IsTrue();

        var prog = result.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(prog).Contains("public record RestockDto");
        await Assert.That(prog).Contains("[Range(0, 1000)]\n    public long amount { get; init; }");
        // An action with no constrained target emits no [Range] on its params.
        var compiler2 = new Compiler();
        var result2 = compiler2.Compile("""
            domain Demo
            Book: entity {
              Title: Text

              Rename: action (value: Text) {
                assign Title to value
              }
            }
            """, CompileMode.All, DbmsPack.Generic);
        await Assert.That(result2.Success).IsTrue();
        var prog2 = result2.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(prog2).Contains("public record RenameDto");
        await Assert.That(prog2).DoesNotContain("[Range");
    }

    [Test]
    public async Task DslCompiler_Dtos_PropagateStringConstraints() {
        // Transport: length/pattern/required propagate onto DTO contracts like range.
        // Entity create DTOs carry the property's DECLARED constraints; action DTOs
        // derive them IMPLICITLY from the effects (a param assigned into a constrained
        // property inherits that property's envelope, merged by intersection).
        var compiler = new Compiler();
        var result = compiler.Compile("""
            domain Demo
            Book: entity {
              Title: Text required length(2, 50)
              Code: Text pattern("^[A-Z]{2}-[0-9]{3}$")
              Pages: Number range(1, 10000)

              Rename: action (value: Text) {
                assign Title to value
              }

              SetCode: action (value: Text) {
                assign Code to value
              }
            }
            """, CompileMode.All, DbmsPack.Generic);
        await Assert.That(result.Success).IsTrue();

        var prog = result.Files!.Single(f => f.FileName == "Program.cs").Source;
        // Entity DTO: declared required + length.
        await Assert.That(prog).Contains("[Required]\n    [MinLength(2)]\n    [MaxLength(50)]\n    public string Title { get; init; }");
        // Entity DTO: declared pattern.
        await Assert.That(prog).Contains("[RegularExpression(\"^[A-Z]{2}-[0-9]{3}$\")]\n    public string Code { get; init; }");
        // Action DTO: required + length inherited from `assign Title to value`.
        await Assert.That(prog).Contains("[Required]\n    [MinLength(2)]\n    [MaxLength(50)]\n    public string value { get; init; }");
        // Action DTO: pattern inherited from `assign Code to value`.
        await Assert.That(prog).Contains("[RegularExpression(\"^[A-Z]{2}-[0-9]{3}$\")]\n    public string value { get; init; }");
    }

    [Test]
    public async Task DslCompiler_ActionDto_EnumTypedParamDeclaresAllowedUnion() {
        // Transport: an enum-typed action parameter's DTO member declares the enum union
        // via [EnumDataType(typeof(EnumName))] — the same propagation as the create DTO.
        var compiler = new Compiler();
        var result = compiler.Compile("""
            domain Demo
            Genre: enum { Fiction, NonFiction }
            Book: entity {
              Genre: Genre

              Categorize: action (genre: Genre) {
                assign Genre to genre
              }
            }
            """, CompileMode.All, DbmsPack.Generic);
        await Assert.That(result.Success).IsTrue();

        var prog = result.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(prog).Contains("public record CategorizeDto\n{\n    [EnumDataType(typeof(Genre))]\n    public Genre genre { get; init; }");
    }

    [Test]
    public async Task DslCompiler_DemoHttp_BodyMatchesDtoFromConstructorMetadata() {
        var compiler = new Compiler();
        var result = compiler.Compile("""
            domain Demo
            Item: entity { Name: Text Qty: Number }
            """, CompileMode.All, DbmsPack.Generic);
        await Assert.That(result.Success).IsTrue();

        var http = result.Files!.Single(f => f.FileName == "demo.http").Source;
        // POST body lists the create-scalar props (Name, Qty), mirroring the DTO.
        await Assert.That(http).Contains("\"Name\": \"sample\"");
        await Assert.That(http).Contains("\"Qty\": 0");
    }

    [Test]
    public async Task OpenRange_VerifiedEnvelope_KeepsBoundOpen() {
        // Hardening: an open range bound (range(0, )) must stay open through the verified
        // envelope. Convert.ToDouble(null) returns 0 (not null), which previously collapsed
        // the max to 0 — emitting a bogus CHECK `qty <= 0` and [Range(0, 0)].
        var compiler = new Compiler();
        var result = compiler.Compile("""
            domain Demo
            Item: entity { Qty: Number range(0, ) }
            """, CompileMode.All, DbmsPack.Generic);
        await Assert.That(result.Success).IsTrue();

        var db = result.Files!.Single(f => f.FileName == "DemoDbContext.cs").Source;
        await Assert.That(db).Contains("qty >= 0");
        await Assert.That(db).DoesNotContain("qty <= 0");

        var prog = result.Files!.Single(f => f.FileName == "Program.cs").Source;
        // Open max caps at the CLR type bound (long.MaxValue as double), never 0.
        await Assert.That(prog).Contains("[Range(0, 9.223372036854776E+18)]");
        await Assert.That(prog).DoesNotContain("[Range(0, 0)]");
    }

    [Test]
    public async Task ActionDto_ConditionalAssigns_EmitNoFalseRejectRange() {
        // Hardening: a param that flows into DIFFERENT targets on DIFFERENT branches has
        // no single provable envelope. The old code intersected the branch ranges — amount
        // would get [Range(0, 10)] (Bonus ∩ Stock) and falsely reject amount=100 which is
        // valid via the vip==2 branch (Stock accepts up to 1000). Fail-closed: no [Range].
        var compiler = new Compiler();
        var result = compiler.Compile("""
            domain Demo
            Book: entity {
              Stock: Number range(0, 1000)
              Bonus: Number range(0, 10)

              Adjust: action (amount: Number, vip: Number) {
                if (vip == 1) {
                  assign Bonus to amount
                }
                if (vip == 2) {
                  assign Stock to amount
                }
              }
            }
            """, CompileMode.All, DbmsPack.Generic);
        await Assert.That(result.Success).IsTrue();

        var prog = result.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(prog).Contains("public record AdjustDto");
        await Assert.That(prog).DoesNotContain("[Range(0, 10)]\n    public long amount");
    }

    [Test]
    public async Task ActionDto_CreateInInitializer_PropagatesTargetRange() {
        // Hardening: a param that flows into a created related entity's constrained property
        // (create in lines { Qty: qty }) inherits the target's range on the action DTO.
        var compiler = new Compiler();
        var result = compiler.Compile("""
            domain Demo
            Order: entity {
              lines: many Line

              AddLine: action (qty: Number) {
                create in lines { Qty: qty }
              }
            }
            Line: entity { Qty: Number range(1, 50) }
            """, CompileMode.All, DbmsPack.Generic);
        await Assert.That(result.Success).IsTrue();

        var prog = result.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(prog).Contains("[Range(1, 50)]\n    public long qty { get; init; }");
    }
}