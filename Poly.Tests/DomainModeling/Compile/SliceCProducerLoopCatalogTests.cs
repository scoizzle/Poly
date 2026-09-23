using System.Text.RegularExpressions;
using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;

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
    public async Task SliceC_AfterLower_SessionHasNonEmptyArtifactCatalog_WithSyntaxModule() {
        var (domain, analysis, session) = Evolve("""
            domain Parking
            Permit: entity { Plate: Text required }
            """);

        await Assert.That(session.ArtifactCatalog.Count).IsEqualTo(0);

        var module = session.Lower(domain, analysis);
        await Assert.That(module.Count).IsGreaterThan(0);
        await Assert.That(session.ArtifactCatalog.Count).IsGreaterThan(0);
        await Assert.That(session.ArtifactCatalog.Any(a =>
            a.Kind == "SyntaxModule" && a.Name == "module" && a.Source == "Lower")).IsTrue();
    }

    [Test]
    public async Task SliceC_HttpAndPersistence_HostFilesComeFromProducerLoop() {
        var dslCompilerPath = FindDslCompilerSource();
        var compilerSource = await File.ReadAllTextAsync(dslCompilerPath);
        // Load-time registration is required; mid-compile bag invent is forbidden.
        await Assert.That(compilerSource.Contains(
            "builder.AddArtifactContributor(new MinimalApiHostArtifactContributor",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(compilerSource.Contains(
            "builder.AddArtifactContributor(new DbContextArtifactContributor",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(Regex.IsMatch(
            compilerSource,
            @"GetMetadata<HttpSurfaceMetadata>[\s\S]{0,400}new MinimalApiHostArtifactContributor")).IsFalse();
        await Assert.That(Regex.IsMatch(
            compilerSource,
            @"GetMetadata<PersistenceSurfaceMetadata>[\s\S]{0,400}new DbContextGenerator")).IsFalse();

        var result = new Compiler().Compile(HttpHostDomain, CompileMode.Entities, DbmsPack.Sqlite);
        await Assert.That(result.Success).IsTrue().Because(
            result.Errors is null ? "" : string.Join("; ", result.Errors));
        var names = result.Files!.Select(f => f.FileName).ToList();
        await Assert.That(names.Contains("Program.cs")).IsTrue();
        await Assert.That(names.Contains("demo.http")).IsTrue();
        await Assert.That(names.Any(n => n.EndsWith("DbContext.cs", StringComparison.Ordinal))).IsTrue();
        await Assert.That(names.Contains("Item.cs")).IsTrue();
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
        await Assert.That(files.Select(f => f.FileName).ToList()).IsEquivalentTo(["Program.cs", "demo.http"]);
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

        public IReadOnlyList<(string FileName, string Source)> Contribute(
            Domain domain, AnalysisResult analysis) {
            Called = true;
            return [("track.txt", domain.Name)];
        }
    }

    private static string FindDslCompilerSource() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            var candidate = Path.Combine(dir.FullName, "src", "Poly.DslCompiler", "DslCompiler.cs");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate src/Poly.DslCompiler/DslCompiler.cs");
    }

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) Evolve(string poly) {
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
