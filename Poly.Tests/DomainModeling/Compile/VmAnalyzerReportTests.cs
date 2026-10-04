using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DslCompiler;
using Poly.Packs.Sqlite;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// Report only: runs the VM analyzer the way <see cref="DomainSession.Emit"/> does over
/// every sample domain and prints what it finds. Emit passes the analysis to the C#
/// generator but ignores its diagnostics today, so the counts say how much would break
/// if Emit started refusing VM errors. Nothing here fails on the counts.
/// Run with <c>--output Detailed</c> to see the table.
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
    private static readonly string[] InvalidProbes = ["nested-invoke-type-mismatch.poly"];

    [Test]
    public async Task SampleDomains_FindsTheSamples() =>
        await Assert.That(SampleDomains().Count()).IsGreaterThanOrEqualTo(10);

    [Test]
    public void ReportVmAnalysis() {
        var rows = SampleDomains().Select(path => Measure(path)).ToList();

        Console.WriteLine("VM analyzer over sample domains (report only)");
        Console.WriteLine("| domain | outcome | types | VM errors | VM warnings |");
        Console.WriteLine("|---|---|---|---|---|");
        foreach (var r in rows)
            Console.WriteLine($"| {r.Path} | {r.Outcome} | {r.Types} | {r.Errors} | {r.Warnings} |");
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

    private sealed record Row(string Path, string Outcome, int Types, int Errors, int Warnings);

    private static Row Measure(string relativePath) {
        var name = InvalidProbes.Contains(Path.GetFileName(relativePath)) ? relativePath + " (invalid by design)" : relativePath;
        try {
            var poly = File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));
            var session = DomainSession.ForSource(poly, Seed, Catalog);
            var changes = DomainCompilation.WithSeed(new PolyDslParser(poly, session).Parse(), Seed);
            var outcome = new DomainEvolution(new Domain("_", [])).Apply(changes, session: session);
            // Not Succeeded covers domain analysis Errors and a change that failed to apply.
            if (!outcome.Succeeded)
                return new Row(name, "domain has Errors", 0, 0, 0);

            // These two calls mirror the start of DomainSession.Emit.
            var types = session.Lower(outcome.Root, outcome.Analysis);
            var vm = DomainSession.TryAnalyzeForEmit(types);
            // Null only means Lower produced no types; an analyzer exception lands in the catch below.
            if (vm is null)
                return new Row(name, "TryAnalyzeForEmit null", types.Count, 0, 0);

            var errors = vm.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            var warnings = vm.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
            return new Row(name, errors > 0 ? "VM Errors" : warnings > 0 ? "VM Warnings" : "VM clean", types.Count, errors, warnings);
        }
        catch (Exception ex) {
            // A report keeps going: the failure shows in this domain's row.
            return new Row(name, $"load failed: {ex.GetType().Name}", 0, 0, 0);
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
