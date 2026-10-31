using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Http;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation;
using Poly.Packs.Sqlite;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// Pins that every maintained non-invalid sample domain is VM-clean: VM analysis
/// reports 0 Errors and 0 Warnings. The two invalid-by-design probes still fail
/// domain analysis. Run with <c>--output Detailed</c> to see the table.
/// </summary>
public sealed class VmAnalyzerReportTests {
    private static readonly ExtensionCatalog Catalog = ExtensionCatalog.Core
        .With(new SqliteLibrary())
        .With(new HttpLibrary());

    private static readonly string[] Seed = [.. ExtensionCatalog.ProductAuthoring, "sqlite"];

    // The live probes and the demo. The archive is left out: it is not maintained and
    // 114 of its 256 files do not load or fail domain analysis.
    private static readonly string[] SampleRoots = ["docs/probes", "demo/live"];

    // Invalid by design: the domain analyzer rejects these. The same list is in ParityTests.
    private static readonly string[] InvalidProbes = [
        "nested-invoke-type-mismatch.poly",
        "restaurant.poly",
    ];

    // Rejected by domain analysis (not just VM analysis). restaurant.poly stores a
    // domain `value` type as an entity property (DMSTR006).
    private static readonly string[] DomainErrorSamples = [
        "docs/probes/dogfood/nested-invoke-type-mismatch.poly",
        "docs/probes/scratch-2026-10-14/restaurant.poly",
    ];

    [Test]
    public async Task SampleDomains_FindsTheSamples() =>
        await Assert.That(SampleDomains().Count()).IsGreaterThanOrEqualTo(10);

    [Test]
    public async Task MaintainedNonInvalidSamples_AreVmClean() {
        var rows = SampleDomains().Select(Measure).ToList();
        var invalid = DomainErrorSamples.ToHashSet(StringComparer.Ordinal);
        var notClean = rows
            .Where(r => !invalid.Contains(r.Path))
            .Where(r => r.Outcome != "VM clean")
            .Select(r => $"{r.Path}: {r.Outcome} (errors {r.Errors}, warnings {r.Warnings}) [{r.ErrorMessages}]")
            .ToArray();

        await Assert.That(notClean).IsEmpty();
        await Assert.That(rows.Where(r => invalid.Contains(r.Path)).All(r => r.Outcome == "domain has Errors")).IsTrue();
    }

    [Test]
    public void ReportVmAnalysis() {
        var rows = SampleDomains().Select(path => Measure(path)).ToList();

        Console.WriteLine("VM analyzer over sample domains");
        Console.WriteLine("| domain | outcome | types | VM errors | VM warnings | distinct errors |");
        Console.WriteLine("|---|---|---|---|---|---|");
        foreach (var r in rows) {
            var label = InvalidProbes.Contains(Path.GetFileName(r.Path))
                ? r.Path + " (invalid by design)"
                : r.Path;
            Console.WriteLine($"| {label} | {r.Outcome} | {r.Types} | {r.Errors} | {r.Warnings} | {r.ErrorMessages} |");
        }
        Console.WriteLine();
        Console.WriteLine($"sample domains: {rows.Count}");
        foreach (var group in rows.GroupBy(r => r.Outcome).OrderBy(g => g.Key, StringComparer.Ordinal))
            Console.WriteLine($"  {group.Key}: {group.Count()}");
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

    private sealed record Row(string Path, string Outcome, int Types, int Errors, int Warnings, string ErrorMessages);

    private static Row Measure(string relativePath) {
        try {
            var poly = File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));
            var session = DomainSession.ForSource(poly, Seed, Catalog);
            var changes = DomainCompilation.WithSeed(new PolyDslParser(poly, session).Parse(), Seed);
            var outcome = new DomainEvolution(new Domain("_", [])).ApplyAndAnalyze(changes, session: session);
            // Not Succeeded covers domain analysis Errors and a change that failed to apply.
            if (!outcome.Succeeded)
                return new Row(relativePath, "domain has Errors", 0, 0, 0, "");

            // These two calls mirror the start of DomainSession.Emit.
            var types = session.Lower(outcome.Root, outcome.Analysis);
            var vm = Interpreter.Analyzer.Analyze(new CompilationUnitNode([], null, types, null));

            var errors = vm.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            var warnings = vm.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
            var errorMessages = string.Join(" | ", vm.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
            return new Row(relativePath, errors > 0 ? "VM Errors" : warnings > 0 ? "VM Warnings" : "VM clean", types.Count, errors, warnings, errorMessages);
        }
        catch (Exception ex) {
            // A report keeps going: the failure shows in this domain's row.
            return new Row(relativePath, $"load failed: {ex.GetType().Name}", 0, 0, 0, "");
        }
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