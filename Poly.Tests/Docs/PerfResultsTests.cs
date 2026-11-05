using System.Text.RegularExpressions;

namespace Poly.Tests.Docs;

/// <summary>
/// The performance document quotes concrete result files, and a hand-typed table drifts from the
/// CSV it claims to come from. This is the cheap half of that guard: every CSV the document names
/// must exist, and the document must say which run its tables came from.
/// <para>
/// It cannot prove the numbers match — that needs a generator — but it does catch a table pointing
/// at a deleted or renamed run, and a table with no provenance at all.
/// </para>
/// </summary>
public partial class PerfResultsTests {
    [Test]
    public async Task EveryQuotedResultsCsv_Exists() {
        var root = RepoRoot();
        var document = Path.Combine(root, "docs", "perf-comparison", "README.md");
        var quoted = ResultsCsv().Matches(File.ReadAllText(document))
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        await Assert.That(quoted).IsNotEmpty()
            .Because("the document must name at least one run it was generated from");

        var missing = quoted
            .Where(name => !File.Exists(Path.Combine(root, "docs", "perf-comparison", "results", name + ".csv")))
            .ToList();
        await Assert.That(missing).IsEmpty()
            .Because($"these CSVs are quoted but not in results/: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// A ratio column is only meaningful when both sides ran in one environment. The document says
    /// which columns are host rows and which are container rows; the table that carries a ratio
    /// must say its ratio does not cross the boundary.
    /// </summary>
    [Test]
    public async Task RatioTable_StatesItsEnvironment() {
        var root = RepoRoot();
        var text = File.ReadAllText(Path.Combine(root, "docs", "perf-comparison", "README.md"));
        await Assert.That(text).Contains("the `Ratio` column is Poly ÷ C# native")
            .Because("a ratio across the host/container boundary is not a comparison");
        await Assert.That(text).Contains("not the newest CSV")
            .Because("the quoted table is older than the newest run and must say so");
    }

    [GeneratedRegex(@"results/(results_\d{8}_\d{6})\.csv")]
    private static partial Regex ResultsCsv();

    private static string RepoRoot() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Poly.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Poly.slnx not found above the test output directory");
    }
}