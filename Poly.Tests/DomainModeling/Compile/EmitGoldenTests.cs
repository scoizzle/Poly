using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DslCompiler;
using Poly.Packs.Sqlite;

using CompileMode = Poly.DslCompiler.CompileMode;
using Compiler = Poly.DslCompiler.DslCompiler;
using DbmsPack = Poly.DslCompiler.DbmsPack;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// Text snapshots of <see cref="DomainSession.Emit"/> (and the session catalog
/// after Emit) for every sample domain, plus DslCompiler host files for CRM.
/// </summary>
public sealed class EmitGoldenTests {
    private static readonly ExtensionCatalog Catalog = ExtensionCatalog.Core
        .With(new SqliteLibrary())
        .With(new HttpLibrary());

    // DslCompiler seed for CompileMode.All + DbmsPack.Sqlite (HttpLibrary is catalog-only here).
    private static readonly string[] Seed = [.. ExtensionCatalog.ProductAuthoring, "sqlite"];

    public static IEnumerable<string> SampleDomains() => [
        "docs/probes/fleet-eval/09-transport/warehouse.poly",
        "docs/probes/fleet-eval/09-transport/orders.poly",
        "docs/probes/fleet-eval/09-transport/clinic.poly",
        "docs/probes/fleet-eval/12-mcp/mcp-library.poly",
        "docs/probes/dogfood/university.poly",
        "docs/probes/dogfood/crm.poly",
        "docs/probes/dogfood/hotel.poly",
        "docs/probes/dogfood/simulate-create-type.poly",
        "docs/probes/dogfood/simulate-create-in.poly",
        "docs/probes/dogfood/simulate-create-create-in.poly",
    ];

    [Test]
    [MethodDataSource(nameof(SampleDomains))]
    public async Task Emit_MatchesGolden(string relativePath) {
        var poly = await File.ReadAllTextAsync(Path.Combine(FindRepoRoot(), relativePath));
        var (files, catalogText) = CompileSample(poly);
        var produced = files.ToDictionary(
            f => f.FileName + ".golden",
            f => f.Source,
            StringComparer.Ordinal);
        produced["catalog.golden"] = catalogText;
        await AssertGoldens(
            Path.Combine(GoldenRoot(), Path.GetFileNameWithoutExtension(relativePath)!),
            produced);
    }

    [Test]
    public async Task Crm_DslCompilerHostFiles_MatchGolden() {
        var poly = await File.ReadAllTextAsync(
            Path.Combine(FindRepoRoot(), "docs/probes/dogfood/crm.poly"));
        var result = new Compiler()
            .Load(new HttpLibrary())
            .Compile(poly, CompileMode.All, DbmsPack.Sqlite);
        if (!result.Success)
            throw new InvalidOperationException(
                $"DslCompiler failed: {string.Join("; ", result.Errors ?? [])}");

        var emitNames = CompileSample(poly).Files
            .Select(f => f.FileName)
            .ToHashSet(StringComparer.Ordinal);
        var hostFiles = result.Files!
            .Where(f => !emitNames.Contains(f.FileName))
            .ToList();
        var produced = hostFiles.ToDictionary(
            f => f.FileName + ".golden",
            f => f.Source,
            StringComparer.Ordinal);
        await AssertGoldens(Path.Combine(GoldenRoot(), "crm-dslcompiler"), produced);
    }

    [Test]
    [MethodDataSource(nameof(SampleDomains))]
    public async Task Emit_IsReproducible(string relativePath) {
        var poly = await File.ReadAllTextAsync(Path.Combine(FindRepoRoot(), relativePath));

        var first = CompileSample(poly);
        var second = CompileSample(poly);
        await AssertSameOutput(first, second);

        var (session, domain, analysis) = AnalyzeSample(poly);
        var once = session.Emit(domain, analysis);
        var catalogOnce = CatalogText(session);
        var twice = session.Emit(domain, analysis);
        var catalogTwice = CatalogText(session);
        await AssertSameOutput((once, catalogOnce), (twice, catalogTwice));
    }

    private static (IReadOnlyList<(string FileName, string Source)> Files, string CatalogText)
        CompileSample(string poly) {
        var (session, domain, analysis) = AnalyzeSample(poly);
        var files = session.Emit(domain, analysis);
        return (files, CatalogText(session));
    }

    private static (DomainSession Session, Domain Domain, AnalysisResult Analysis)
        AnalyzeSample(string poly) {
        var session = DomainSession.ForSource(poly, Seed, Catalog);
        var changes = DomainCompilation.WithSeed(new PolyDslParser(poly, session).Parse(), Seed);
        var outcome = new DomainEvolution(new Domain("_", [])).Apply(changes, session: session);
        if (!outcome.Succeeded) {
            var errors = string.Join("; ", outcome.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new InvalidOperationException($"AnalyzeSample failed: {errors}");
        }
        return (session, outcome.Root, outcome.Analysis);
    }

    private static string CatalogText(DomainSession session) => session.ArtifactCatalog.ToText();

    // Set POLY_UPDATE_GOLDEN=1 to write snapshots instead of comparing.
    private static async Task AssertGoldens(string dir, Dictionary<string, string> produced) {
        if (Environment.GetEnvironmentVariable("POLY_UPDATE_GOLDEN") == "1") {
            Directory.CreateDirectory(dir);
            foreach (var (name, text) in produced)
                await File.WriteAllTextAsync(Path.Combine(dir, name), Normalize(text));
            foreach (var path in Directory.GetFiles(dir, "*.golden")) {
                if (!produced.ContainsKey(Path.GetFileName(path)))
                    File.Delete(path);
            }
            return;
        }

        var onDisk = Directory.GetFiles(dir, "*.golden").Select(p => Path.GetFileName(p)!).ToArray();
        await Assert.That(onDisk.Order(StringComparer.Ordinal).ToArray())
            .IsEquivalentTo(produced.Keys.Order(StringComparer.Ordinal).ToArray());
        foreach (var (name, text) in produced) {
            var expected = Normalize(await File.ReadAllTextAsync(Path.Combine(dir, name)));
            await Assert.That(Normalize(text)).IsEqualTo(expected);
        }
    }

    private static async Task AssertSameOutput(
        (IReadOnlyList<(string FileName, string Source)> Files, string CatalogText) left,
        (IReadOnlyList<(string FileName, string Source)> Files, string CatalogText) right) {
        await Assert.That(string.Join("\n", left.Files.Select(f => f.FileName)))
            .IsEqualTo(string.Join("\n", right.Files.Select(f => f.FileName)));
        for (var i = 0; i < left.Files.Count; i++)
            await Assert.That(Normalize(left.Files[i].Source)).IsEqualTo(Normalize(right.Files[i].Source));
        await Assert.That(Normalize(left.CatalogText)).IsEqualTo(Normalize(right.CatalogText));
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    private static string GoldenRoot() =>
        Path.Combine(FindRepoRoot(), "Poly.Tests", "DomainModeling", "Compile", "EmitGolden");

    private static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir, "Poly.sln"))
                || File.Exists(Path.Combine(dir, "docs/CORE.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root from " + AppContext.BaseDirectory);
    }
}
