using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Http;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Sqlite;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// Fails closed at the Roslyn boundary: every maintained non-invalid sample's
/// emitted files compile in one CSharpCompilation with warnings-as-errors
/// (0 errors, 0 warnings). Domain-analysis validity is a precondition, already
/// pinned by <see cref="VmAnalyzerReportTests.MaintainedNonInvalidSamples_AreVmClean"/>.
/// </summary>
public sealed class SampleRoslynGateTests {
    private static readonly ExtensionCatalog Catalog = ExtensionCatalog.Core
        .With(new SqliteLibrary())
        .With(new HttpLibrary());

    private static readonly string[] Seed = [.. ExtensionCatalog.ProductAuthoring, "sqlite"];

    // The live probes and the demo. The archive is left out: it is not maintained.
    private static readonly string[] SampleRoots = ["docs/probes", "demo/live"];

    // Invalid by design: the domain analyzer rejects these. The same list is in
    // VmAnalyzerReportTests and ParityTests.
    private static readonly string[] InvalidByDesign = [
        "nested-invoke-type-mismatch.poly",
        "restaurant.poly",
    ];

    [Test]
    public async Task MaintainedSamples_CompileWithWarningsAsErrors() {
        var notClean = SampleDomains()
            .Where(p => !InvalidByDesign.Contains(Path.GetFileName(p), StringComparer.Ordinal))
            .Select(Measure)
            .Where(r => r.Diagnostics.Length > 0)
            .Select(r => $"{r.Path}: {string.Join(" | ", r.Diagnostics)}")
            .ToArray();

        await Assert.That(notClean).IsEmpty();
        await Assert.That(SampleDomains().Count()).IsGreaterThanOrEqualTo(10);
    }

    private sealed record Row(string Path, string[] Diagnostics);

    private static Row Measure(string relativePath) {
        var poly = File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));
        var session = DomainSession.ForSource(poly, Seed, Catalog);
        var changes = DomainCompilation.WithSeed(new PolyDslParser(poly, session).Parse(), Seed);
        var outcome = new DomainEvolution(new Domain("_", [])).Apply(changes, session: session);
        if (!outcome.Succeeded) {
            return new Row(relativePath, [$"domain has Errors: {string.Join("; ", outcome.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message))}"]);
        }

        IReadOnlyList<(string FileName, string Source)> files;
        try {
            files = session.Emit(outcome.Root, outcome.Analysis);
        }
        catch (InvalidOperationException ex) {
            return new Row(relativePath, [$"Emit refused: {ex.Message}"]);
        }
        if (files.Count == 0)
            return new Row(relativePath, ["Emit produced no files."]);

        var diagnostics = ExportedCSharp.CompileWithWarningsAsErrors(
                "Sample_" + Path.GetFileNameWithoutExtension(relativePath), files)
            .Select(d => d.ToString())
            .ToArray();
        return new Row(relativePath, diagnostics);
    }

    private static IEnumerable<string> SampleDomains() {
        var root = FindRepoRoot();
        return SampleRoots
            .Select(r => Path.Combine(root, r))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.poly", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);
    }

    private static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir, "docs/CORE.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root from " + AppContext.BaseDirectory);
    }
}