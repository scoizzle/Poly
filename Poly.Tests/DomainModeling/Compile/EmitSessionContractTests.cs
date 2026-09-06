using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DslCompiler;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Packs.Sqlite;
using Poly.Tests.TestHelpers;

using Compiler = Poly.DslCompiler.DslCompiler;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// Contract tests for emit-session: <c>uses</c> selects analyzers and bags;
/// downstream IR (entity module, DbContext, HTTP, lowered operations) is
/// asserted as Syntax trees — not rendered C# text.
/// </summary>
public sealed class EmitSessionContractTests {
    private static readonly ExtensionCatalog Catalog = ExtensionCatalog.Core
        .With(new SqliteLibrary())
        .With(new HttpLibrary());

    private const string CatalogSqlite = """
        domain Catalog
        uses temporal
        uses storage
        uses sqlite

        Item: entity {
          Name: Text
          Qty: Number
          Active: Boolean
        }
        """;

    private const string CatalogLanguageOnly = """
        domain Catalog
        uses temporal
        uses storage

        Item: entity {
          Name: Text
          Qty: Number
          Active: Boolean
        }
        """;

    private const string CatalogHttpSqlite = """
        domain Catalog
        uses temporal
        uses storage
        uses sqlite
        uses http

        Item: entity {
          Name: Text
        }
        """;

    private const string TemporalNowPolicy = """
        domain T
        uses temporal

        Item: entity {
          Expiry: Date
          Due: policy { Expiry < Now }
        }
        """;

    private static (DomainSession Session, Domain Domain, AnalysisResult Analysis) AnalyzePoly(string poly) {
        var session = DomainSession.ForSource(poly, seed: [], catalog: Catalog);
        var changes = new PolyDslParser(poly, session).Parse();
        var outcome = new DomainEvolution(new Domain("_", [])).Apply(changes, session: session);
        if (!outcome.Succeeded) {
            var errors = string.Join("; ", outcome.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new InvalidOperationException($"AnalyzePoly failed: {errors}");
        }
        var domain = outcome.Root;
        session = session.WithDomain(domain);
        return (session, domain, session.Analyze(domain));
    }

    private static CompilationUnitNode DbContextIr(Domain domain, AnalysisResult analysis) {
        var storage = analysis.GetMetadata<StorageMappingMetadata>(domain)?.Storage
            ?? throw new InvalidOperationException("Missing StorageMappingMetadata.");
        return new DbContextGenerator(domain, storage).GenerateCompilationUnit();
    }

    private static CompilationUnitNode HttpIr(Domain domain, AnalysisResult analysis) {
        var storage = analysis.GetMetadata<StorageMappingMetadata>(domain)?.Storage
            ?? throw new InvalidOperationException("Missing StorageMappingMetadata.");
        var behavior = BehaviorMetadata.From(domain, analysis);
        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)?.Aggregate
            ?? throw new InvalidOperationException("Missing OwnershipAggregateMetadata.");
        return new MinimalApiGenerator(domain, analysis, storage, behavior, aggregate)
            .GenerateCompilationUnit($"{domain.Name}DbContext");
    }

    private static bool HasColumnType(MethodDefinitionNode onModelCreating, string sqlType) =>
        onModelCreating.FindInvocations("HasColumnType")
            .Any(i => i.Arguments is [Constant { Value: var v }, ..]
                && string.Equals(v?.ToString(), sqlType, StringComparison.Ordinal));

    [Test]
    public async Task UsesSqlite_PublishesPersistenceBag_AndSqliteColumnTypesOnDbContextIr() {
        var (session, domain, analysis) = AnalyzePoly(CatalogSqlite);
        await Assert.That(session.Extensions).Contains("sqlite");
        await Assert.That(analysis.GetMetadata<PersistenceSurfaceMetadata>(domain)).IsNotNull();
        await Assert.That(analysis.GetMetadata<HttpSurfaceMetadata>(domain)).IsNull();

        var storage = analysis.GetMetadata<StorageMappingMetadata>(domain)?.Storage;
        await Assert.That(storage).IsNotNull();
        var item = storage!.Entities.Single(e => e.Name == "Item");
        await Assert.That(item.Columns.Single(c => c.Name == "Name").ColumnType).IsEqualTo("TEXT");
        await Assert.That(item.Columns.Single(c => c.Name == "Qty").ColumnType).IsEqualTo("INTEGER");
        await Assert.That(item.Columns.Single(c => c.Name == "Active").ColumnType).IsEqualTo("INTEGER");

        var ctxType = DbContextIr(domain, analysis).FindType("CatalogDbContext");
        await Assert.That(ctxType).IsNotNull();
        var omc = ctxType!.FindMethod("OnModelCreating");
        await Assert.That(omc).IsNotNull();
        await Assert.That(HasColumnType(omc!, "TEXT")).IsTrue();
        await Assert.That(HasColumnType(omc!, "INTEGER")).IsTrue();
        await Assert.That(ctxType.FindProperty("Items")).IsNotNull();
    }

    [Test]
    public async Task WithoutSqlite_NoPersistenceBag_EntityModuleHasItemType() {
        var (session, domain, analysis) = AnalyzePoly(CatalogLanguageOnly);
        await Assert.That(session.Extensions.Contains("sqlite")).IsFalse();
        await Assert.That(analysis.GetMetadata<PersistenceSurfaceMetadata>(domain)).IsNull();

        var types = DomainProgramProjection.ToSyntax(domain, analysis);
        var item = types.Single(t => t.Name == "Item");
        await Assert.That(item.Properties?.Any(p => p.Name == "Name")).IsTrue();
        await Assert.That(item.Properties?.Any(p => p.Name == "Qty")).IsTrue();

        var files = session.Emit(domain, analysis);
        await Assert.That(files.Any(f => f.FileName == "Item.cs")).IsTrue();
        await Assert.That(files.Any(f => f.FileName.EndsWith("DbContext.cs", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Emit_ProjectedTypesIncludingGenerics_AnalyzeWithoutFallback() {
        const string poly = """
            domain Hotel
            uses temporal

            Industry: enum { Hospitality, Manufacturing }

            Stay: entity {
              Guest: Text
              Industry: Industry
              Vacant: stage {
                CheckIn: action { transition to Occupied }
              }
              Occupied: stage { }
            }

            Room: entity {
              Number: Text
              stays: many Stay
              Book: action (guest: Text) -> Stay {
                create in stays { Guest: guest }
              }
            }
            """;

        var (session, domain, analysis) = AnalyzePoly(poly);
        var types = DomainProgramProjection.ToSyntax(domain, analysis);
        await Assert.That(types.Any(t => t.Name == "DomainResult" && t.GenericParameters is { Count: > 0 })).IsTrue();

        var unit = new CompilationUnitNode([], null, types, null);
        var interp = Poly.Interpretation.Interpreter.Analyzer.Analyze(unit);
        await Assert.That(interp).IsNotNull();

        var domainResultGeneric = types.Single(t => t.Name == "DomainResult" && t.GenericParameters is { Count: > 0 });
        var resolvedGeneric = interp.GetMetadata<TypeDefinitionMetadata>(domainResultGeneric)?.TypeDefinition;
        await Assert.That(resolvedGeneric).IsNotNull();
        var valueType = resolvedGeneric!.Properties.Single(p => p.Name == "Value").MemberTypeDefinition;
        await Assert.That(valueType.Name).IsEqualTo("T");

        var files = session.Emit(domain, analysis);
        await Assert.That(files.Any(f => f.FileName == "Room.cs")).IsTrue();
        await Assert.That(files.Any(f => f.FileName == "Stay.cs")).IsTrue();
        await Assert.That(files.Any(f => f.FileName == "Poly.Types.cs")).IsTrue();
    }

    [Test]
    public async Task UsesHttp_PublishesHttpBag_MinimalApiIrMapsItemCollection() {
        var (_, domain, analysis) = AnalyzePoly(CatalogHttpSqlite);
        await Assert.That(analysis.GetMetadata<HttpSurfaceMetadata>(domain)).IsNotNull();
        await Assert.That(analysis.GetMetadata<PersistenceSurfaceMetadata>(domain)).IsNotNull();

        var unit = HttpIr(domain, analysis);
        await Assert.That(unit.TopLevelInvocationNames().Contains("MapGet")).IsTrue();
        await Assert.That(unit.TopLevelInvocationNames().Contains("AddDbContext")).IsTrue();
        var mapGets = unit.TopLevelStatements?
            .OfType<Invoke>()
            .SelectMany(i => i.FindInvocations("MapGet"))
            .ToList() ?? [];
        await Assert.That(mapGets.Any(i =>
            i.Arguments.Length > 0
            && i.Arguments[0] is Constant { Value: string route }
            && route.Contains("/api/items", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task WithoutHttp_NoHttpBag() {
        var (_, domain, analysis) = AnalyzePoly(CatalogSqlite);
        await Assert.That(analysis.GetMetadata<HttpSurfaceMetadata>(domain)).IsNull();
    }

    [Test]
    public async Task Compile_SourceUsesSqlite_WithoutModeDb_EmitsDbContextFile() {
        var result = new Compiler().Compile(CatalogSqlite);
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Files!.Any(f => f.FileName == "Item.cs")).IsTrue();
        await Assert.That(result.Files!.Any(f => f.FileName == "CatalogDbContext.cs")).IsTrue();
        await Assert.That(result.Files!.Any(f => f.FileName == "Program.cs")).IsFalse();
    }

    [Test]
    public async Task Compile_SourceUsesHttp_WithoutModeAll_EmitsProgramFile() {
        var result = new Compiler().Compile(CatalogHttpSqlite);
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Files!.Any(f => f.FileName == "Program.cs")).IsTrue();
        await Assert.That(result.Files!.Any(f => f.FileName == "demo.http")).IsTrue();
        await Assert.That(result.Files!.Any(f => f.FileName == "CatalogDbContext.cs")).IsTrue();
    }

    [Test]
    public async Task Compile_LanguageOnly_DefaultCompile_NoHostFiles() {
        var result = new Compiler().Compile(CatalogLanguageOnly);
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Files!.Any(f => f.FileName == "Item.cs")).IsTrue();
        await Assert.That(result.Files!.Any(f => f.FileName.EndsWith("DbContext.cs", StringComparison.Ordinal))).IsFalse();
        await Assert.That(result.Files!.Any(f => f.FileName == "Program.cs")).IsFalse();
    }

    [Test]
    public async Task Compile_ModeAll_WithoutUsesHttp_DoesNotEmitProgramFile() {
        var result = new Compiler().Compile(CatalogSqlite, CompileMode.All);
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Files!.Any(f => f.FileName == "Item.cs")).IsTrue();
        await Assert.That(result.Files!.Any(f => f.FileName == "CatalogDbContext.cs")).IsTrue();
        await Assert.That(result.Files!.Any(f => f.FileName == "Program.cs")).IsFalse();
        await Assert.That(result.Files!.Any(f => f.FileName == "demo.http")).IsFalse();
    }

    [Test]
    public async Task Compile_ModeAll_LanguageOnly_SeedsPersistence_EmitsDbContextNotProgram() {
        var result = new Compiler().Compile(CatalogLanguageOnly, CompileMode.All);
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Files!.Any(f => f.FileName == "CatalogDbContext.cs")).IsTrue();
        await Assert.That(result.Files!.Any(f => f.FileName == "Program.cs")).IsFalse();
    }

    [Test]
    public async Task Compile_LoadHttpLibrary_WithoutUsesHttp_EmitsProgramFile() {
        var result = new Compiler()
            .Load(new HttpLibrary())
            .Compile(CatalogLanguageOnly, CompileMode.All);
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Files!.Any(f => f.FileName == "Program.cs")).IsTrue();
        await Assert.That(result.Files!.Any(f => f.FileName == "demo.http")).IsTrue();
    }

    [Test]
    public async Task ParseNow_WithTemporal_LowersToUtcNowMember_WithoutMeaningTable() {
        var (session, domain, _) = AnalyzePoly(TemporalNowPolicy);
        await Assert.That(session.Meaning.Lowering.Handlers).IsEmpty();
        var policy = domain.Types.OfType<Entity>().Single().Policies.Single();
        await Assert.That(policy.Expression).IsTypeOf<Comparison>();
        var cmp = (Comparison)policy.Expression;
        await Assert.That(cmp.Right).IsTypeOf<Now>();

        var lowered = new DomainExpressionLoweringPass(new LoweringContext(new Parameter("entity")))
            .Lower(cmp.Right, new Parameter("entity"));
        await Assert.That(lowered).IsTypeOf<Member>();
        var member = (Member)lowered;
        await Assert.That(member.MemberName).IsEqualTo("UtcNow");
        await Assert.That(member.Value).IsTypeOf<NamedTypeReference>();
        await Assert.That(((NamedTypeReference)member.Value).TypeName).IsEqualTo("DateTime");
    }

    [Test]
    public async Task ParseNow_WithoutTemporal_IsPropertyAccess_NoVocabularyBag() {
        var poly = """
            domain T
            Item: entity {
              Expiry: Date
              Due: policy { Expiry < Now }
            }
            """;
        var session = DomainSession.ForSource(poly, seed: [], catalog: Catalog);
        var changes = new PolyDslParser(poly, session).Parse();
        var addPolicy = changes.OfType<AddPolicyToEntityChange>().Single();
        await Assert.That(addPolicy.Policy.Expression).IsTypeOf<Comparison>();
        await Assert.That(((Comparison)addPolicy.Policy.Expression).Right).IsTypeOf<PropertyAccess>();
        await Assert.That(((PropertyAccess)((Comparison)addPolicy.Policy.Expression).Right).Name)
            .IsEqualTo("Now");

        var outcome = new DomainEvolution(new Domain("_", [])).Apply(changes, session: session);
        var domain = outcome.Root;
        var analysis = session.WithDomain(domain).Analyze(domain);
        await Assert.That(analysis.GetMetadata<TemporalVocabularyMetadata>(domain)).IsNull();
    }

    [Test]
    public async Task Compile_StructuralFailure_EmitsNoFiles() {
        var result = new Compiler().Compile("""
            domain Catalog
            uses temporal
            Item: entity { Name: Text }
            Item: entity { Qty: Number }
            """);
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Files).IsNull();
    }

    [Test]
    public async Task EntityModuleAst_MatchesProjectedTypes() {
        var (session, domain, analysis) = AnalyzePoly(CatalogLanguageOnly);
        var types = DomainProgramProjection.ToSyntax(domain, analysis);
        var files = session.Emit(domain, analysis);
        await Assert.That(files.Select(f => f.FileName).Contains("Item.cs")).IsTrue();
        await Assert.That(types.Any(t => t.Name == "Item")).IsTrue();
        var item = types.Single(t => t.Name == "Item");
        await Assert.That(item.Properties?.Any(p => p.Name == "Name")).IsTrue();
        await Assert.That(item.Properties?.Any(p => p.Name == "Qty")).IsTrue();
    }
}
