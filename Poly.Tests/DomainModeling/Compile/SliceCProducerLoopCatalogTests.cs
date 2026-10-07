using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;

using Artifact = Poly.DomainModeling.Compile.Artifact;
using CompileMode = Poly.DslCompiler.CompileMode;
using Compiler = Poly.DslCompiler.DslCompiler;
using DbContextArtifactContributor = Poly.DslCompiler.DbContextArtifactContributor;
using DbmsPack = Poly.DslCompiler.DbmsPack;
using MinimalApiHostArtifactContributor = Poly.DslCompiler.MinimalApiHostArtifactContributor;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// Slice C oracle: one producer loop + artifact catalog on the session after Lower.
/// Host call-sites come from Load-registered contributors — not DslCompiler invent.
/// </summary>
public class SliceCProducerLoopCatalogTests {
    private const string SampleDomain = """
        domain Library

        Book: entity {
          Title: Text required
          Pages: Number
        }
        """;

    private const string StructurallyInvalidDomain = """
        domain Library

        Book: entity {
          Title: Text
        }

        Book: entity {
          Pages: Number
        }
        """;

    private const string HttpHostDomain = """
        domain Catalog
        uses temporal
        uses storage
        uses sqlite
        uses http

        Item: entity {
          Name: Text required
          Qty: Number
        }
        """;

    [Test]
    public async Task SliceC_AfterLower_SessionHasNonEmptyArtifactCatalog_WithTrees() {
        var (domain, analysis, session) = Evolve("""
            domain Parking
            Permit: entity { Plate: Text required }
            """);

        await Assert.That(session.ArtifactCatalog.Artifacts.Count).IsEqualTo(0);

        var module = session.Lower(domain, analysis);
        await Assert.That(module.Count).IsGreaterThan(0);
        await Assert.That(session.ArtifactCatalog.Artifacts.Count).IsGreaterThan(0);
        await Assert.That(session.ArtifactCatalog.ToText())
            .IsEqualTo("analysis-report|Parking|Analyze\n" +
                       "scaffolding|Parking|Lower\nsource-domain|Parking|Lower\n" +
                       "entity|Parking/Permit|Lower\nsource-entity|Parking/Permit|Lower");
    }

    [Test]
    public async Task SliceC_EachLower_BuildsANewCatalog_NotSharedAcrossSessions() {
        var (domain, analysis, session) = Evolve(SampleDomain);
        var (otherDomain, otherAnalysis, otherSession) = Evolve(SampleDomain);

        session.Lower(domain, analysis);
        otherSession.Lower(otherDomain, otherAnalysis);
        await Assert.That(session.ArtifactCatalog).IsNotSameReferenceAs(otherSession.ArtifactCatalog);

        var first = session.ArtifactCatalog;
        first.DeclareType("type", mayPointAt: []);
        first.Register(new Artifact(
            new ArtifactDescriptor(ArtifactId.Create(["Extra"], "type"), "Test"), Payload: null));
        session.Lower(domain, analysis);

        await Assert.That(session.ArtifactCatalog).IsNotSameReferenceAs(first);
        const string libraryTrees =
            "analysis-report|Library|Analyze\n" +
            "scaffolding|Library|Lower\nsource-domain|Library|Lower\n" +
            "entity|Library/Book|Lower\nsource-entity|Library/Book|Lower";
        await Assert.That(session.ArtifactCatalog.ToText()).IsEqualTo(libraryTrees);
        await Assert.That(first.ToText()).IsEqualTo("type|Extra|Test\n" + libraryTrees);
        await Assert.That(otherSession.ArtifactCatalog.ToText()).IsEqualTo(libraryTrees);
    }

    [Test]
    public async Task SliceC_HttpAndPersistence_HostFilesComeFromProducerLoop() {
        var result = new Compiler().Compile(HttpHostDomain, CompileMode.Entities, DbmsPack.Sqlite);
        await Assert.That(result.Success).IsTrue().Because(
            result.Errors is null ? "" : string.Join("; ", result.Errors));
        var names = result.Files!.Select(f => f.FileName).ToList();
        await Assert.That(names.Contains("Program.cs")).IsTrue();
        await Assert.That(names.Contains("demo.http")).IsTrue();
        await Assert.That(names.Any(n => n.EndsWith("DbContext.cs", StringComparison.Ordinal))).IsTrue();
        await Assert.That(names.Contains("Item.cs")).IsTrue();

        var catalog = result.Catalog!;
        var http = Contributed(catalog, "demo.http");
        var db = catalog.Artifacts.Single(a =>
            a.Descriptor.Id.Type == ContributedFile.Type
            && ContributedFile.FileName(a).EndsWith("DbContext.cs", StringComparison.Ordinal));
        await Assert.That(http.Descriptor.Producer).IsEqualTo(nameof(MinimalApiHostArtifactContributor));
        await Assert.That(db.Descriptor.Producer).IsEqualTo(nameof(DbContextArtifactContributor));
        await Assert.That(TextFile(result, "demo.http")).IsEqualTo(ContributedFile.Text(http));
        await Assert.That(TextFile(result, ContributedFile.FileName(db))).IsEqualTo(ContributedFile.Text(db));
        await Assert.That(TextFile(result, "Program.cs"))
            .IsEqualTo(ContributedFile.Text(Contributed(catalog, "Program.cs")));
        var item = Contributed(catalog, "Item.cs");
        await Assert.That(item.Descriptor.Producer).IsEqualTo("Emit");
        await Assert.That(item.Descriptor.References.Single().ToString()).IsEqualTo("Catalog/Item#entity");
    }

    [Test]
    public async Task SliceC_DirtyAnalyze_ContributorsNotCalled() {
        var contributor = new TrackingContributor();
        var result = new Compiler()
            .AddArtifactContributor(contributor)
            .Compile(StructurallyInvalidDomain, CompileMode.All);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Files).IsNull();
        await Assert.That(contributor.Called).IsFalse();
    }

    [Test]
    public async Task SliceC_ProducerMissingBags_FailsClosed() {
        var httpOnly = """
            domain Thin
            uses temporal
            uses http

            Book: entity {
              Title: Text required
            }
            """;
        var result = new Compiler().Compile(httpOnly, CompileMode.Entities);
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Errors).IsNotNull();
        await Assert.That(result.Errors!.Any(e =>
            e.Contains("HTTP artifacts require", StringComparison.Ordinal)
            || e.Contains("storage", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task SliceC_DbContextContributor_NoPersistBag_NoOps() {
        var (domain, analysis, _) = Evolve(SampleDomain);
        var files = new DbContextArtifactContributor().Contribute(domain, analysis);
        await Assert.That(files.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SliceC_MinimalApiContributor_NoHttpAndNoStorage_NoOps() {
        // NoOp is both-null only: SampleDomain (ProductAuthoring seed) has neither
        // HttpSurfaceMetadata nor StorageMappingMetadata.
        var (domain, analysis, session) = Evolve(SampleDomain);
        _ = session.Lower(domain, analysis);
        var files = new MinimalApiHostArtifactContributor().Contribute(domain, analysis);
        await Assert.That(files.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SliceC_MinimalApiContributor_NoHttpBag_WithStorage_EmitsHostFiles() {
        // Intentional harness emit: storage present + http null still contributes
        // Program.cs + demo.http (Compile path only registers with the http id).
        // persistence (Core) registers StoragePass; no http door.
        var storageOnly = """
            domain Catalog
            uses temporal
            uses storage
            uses persistence

            Item: entity {
              Name: Text required
              Qty: Number
            }
            """;
        var (domain, analysis, session) = Evolve(storageOnly);
        await Assert.That(analysis.GetMetadata<HttpSurfaceMetadata>(domain)).IsNull();
        await Assert.That(analysis.GetMetadata<StorageMappingMetadata>(domain)).IsNotNull();
        _ = session.Lower(domain, analysis);
        var files = new MinimalApiHostArtifactContributor().Contribute(domain, analysis);
        await Assert.That(TextFiles(files).Select(ContributedFile.FileName).ToList())
            .IsEquivalentTo(["Program.cs", "demo.http"]);
    }

    [Test]
    public async Task SliceC_CompileDbmsPack_AlwaysWins_ProgramProvider() {
        // Compile DbmsPack always wins over source uses sqlite for Program.cs provider.
        var generic = new Compiler().Compile(HttpHostDomain, CompileMode.Entities, DbmsPack.Generic);
        await Assert.That(generic.Success).IsTrue().Because(
            generic.Errors is null ? "" : string.Join("; ", generic.Errors));
        var genericProg = generic.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(genericProg).Contains("UseInMemoryDatabase");
        await Assert.That(genericProg).DoesNotContain("UseSqlite");

        var sqlite = new Compiler().Compile(HttpHostDomain, CompileMode.Entities, DbmsPack.Sqlite);
        await Assert.That(sqlite.Success).IsTrue().Because(
            sqlite.Errors is null ? "" : string.Join("; ", sqlite.Errors));
        var sqliteProg = sqlite.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(sqliteProg).Contains("UseSqlite");
        await Assert.That(sqliteProg).DoesNotContain("UseInMemoryDatabase");
    }

    private sealed class TrackingContributor : IArtifactContributor {
        public bool Called { get; private set; }

        public IReadOnlyList<Artifact> Contribute(Domain domain, AnalysisResult analysis) {
            Called = true;
            return [ContributedFile.Create(domain, "track.txt", domain.Name, nameof(TrackingContributor))];
        }
    }

    private static IEnumerable<Artifact> TextFiles(IReadOnlyList<Artifact> artifacts) =>
        artifacts.Where(a => a.Descriptor.Id.Type == ContributedFile.Type);

    private static Artifact Contributed(ArtifactCatalog catalog, string fileName) =>
        catalog.Artifacts.Single(a =>
            a.Descriptor.Id.Type == ContributedFile.Type && ContributedFile.FileName(a) == fileName);

    private static string TextFile(Compiler.CompileResult result, string fileName) =>
        result.Files!.Single(f => f.FileName == fileName).Source;

    internal static (Domain Domain, AnalysisResult Analysis, DomainSession Session) Evolve(string poly) {
        var session = DomainSession.ForSource(poly, ExtensionCatalog.ProductAuthoring);
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, result.Analysis, session);
    }
}