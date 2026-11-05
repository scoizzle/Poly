using System.Globalization;
using System.Text.RegularExpressions;

namespace Poly.Tests.Docs;

/// <summary>
/// The performance document quotes concrete numbers from concrete runs. A hand-typed table drifts:
/// someone re-runs `run.sh`, updates the CSV, and leaves the prose behind. These tests tie the two
/// together — the newest table is checked value by value against the CSV it names — so a number can
/// only change by changing the run it came from.
/// <para>
/// They cannot verify a table that names no CSV, which is why the document also has to say its
/// provenance; <see cref="RatioTable_StatesItsEnvironment"/> checks that it does.
/// </para>
/// </summary>
public partial class PerfResultsTests {
    /// <summary>The run the "Final run" table is generated from, and the languages it compares.</summary>
    private const string FinalRun = "results_20261023_151722";
    private static readonly (string Benchmark, string Csv)[] Benchmarks = [
        ("nqueens", "nqueens"), ("sieve", "sieve"), ("mandelbrot", "mandelbrot"), ("collatz", "collatz"),
    ];

    [Test]
    public async Task EveryQuotedResultsCsv_Exists() {
        var quoted = ResultsCsv().Matches(Document())
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        await Assert.That(quoted).IsNotEmpty()
            .Because("the document must name at least one run it was generated from");

        var missing = quoted
            .Where(name => !File.Exists(Path.Combine(ResultsDirectory(), name + ".csv")))
            .ToList();
        await Assert.That(missing).IsEmpty()
            .Because($"these CSVs are quoted but not in results/: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// The one table that is quoted as "the final run" must equal the CSV it names, row by row.
    /// Changing a number in the prose without re-running is a red test.
    /// </summary>
    [Test]
    public async Task FinalRunTable_MatchesItsCsv() {
        var csv = ReadCsv(Path.Combine(ResultsDirectory(), FinalRun + ".csv"));
        var rows = FinalRunRows();
        await Assert.That(rows.Count).IsEqualTo(Benchmarks.Length)
            .Because("the final-run table should have one row per benchmark");

        foreach (var (benchmark, csvName) in Benchmarks) {
            var row = rows.Single(r => r.Benchmark == benchmark);
            await AssertRow(csv, csvName, "Poly VM", row.PolyAvg, row.PolyMin);
            await AssertRow(csv, csvName, "C# native", row.NativeAvg, null);
            await AssertRow(csv, csvName, "JS", row.Js, null);
            // The ratio is Poly / C# native, both host rows, printed to two decimals.
            await Assert.That(row.Ratio).IsEqualTo(Ratio(csv[(csvName, "Poly VM")].Avg, row.NativeAvg))
                .Because($"{benchmark}: the document says {row.Ratio}x; the CSV implies another ratio");
        }
    }

    /// <summary>
    /// A ratio column is only meaningful when both sides ran in one environment, and a table older
    /// than the newest run must say so. The JS column is a container row and must not be presented
    /// as host.
    /// </summary>
    [Test]
    public async Task RatioTable_StatesItsEnvironment() {
        var text = Document();
        await Assert.That(text).Contains("The `Ratio` column is Poly ÷ C# native")
            .Because("a ratio across the host/container boundary is not a comparison");
        await Assert.That(text).Contains("The `JS` column is a **linux/arm64 Docker** row")
            .Because("JS runs in Docker; calling it a host row was false");
        await Assert.That(text).Contains("not the newest CSV")
            .Because("the quoted table is older than the newest run and must say so");
    }

    // ── the table ────────────────────────────────────────────────────────────

    internal sealed record FinalRow(
        string Benchmark, int PolyAvg, int PolyMin, int NativeAvg, decimal Ratio, int Js);

    /// <summary>Rows of the table under "exec avg (min):", parsed from the Markdown itself.</summary>
    private static List<FinalRow> FinalRunRows() {
        var lines = Document().Split('\n');
        var start = Array.FindIndex(lines, line => line.Contains("exec avg (min):", StringComparison.Ordinal));
        if (start < 0)
            throw new InvalidOperationException("the 'exec avg (min)' table is missing from the document");
        var rows = new List<FinalRow>();
        var inTable = false;
        foreach (var line in lines.Skip(start)) {
            if (!line.StartsWith("|", StringComparison.Ordinal)) {
                // A table ends at the first line that is not a row; a later table is not ours.
                if (inTable)
                    break;
                continue;
            }
            inTable = true;
            var cells = line.Split('|', StringSplitOptions.TrimEntries);
            if (cells.Length < 6 || !NameCell().IsMatch(cells[1]))
                continue;
            // cells: [_, benchmark, poly, native, ratio, js, _]; skip anything that is not a data row.
            if (cells.Length < 7)
                continue;
            var poly = Numbers().Matches(cells[2]).Select(m => Parse(m.Value)).ToList();
            // Pin the shape, not just the values: an extra number in a cell is drift too.
            if (poly.Count != 2)
                throw new InvalidOperationException($"the Poly cell '{cells[2]}' should hold avg and min only");
            if (Numbers().Matches(cells[3]).Count != 1 || Numbers().Matches(cells[5]).Count != 1)
                throw new InvalidOperationException(
                    $"the native/JS cells should hold one number each: '{cells[3]}', '{cells[5]}'");
            var native = Parse(Numbers().Match(cells[3]).Value);
            if (!RatioCell().IsMatch(cells[4]) || Numbers().Matches(cells[4]).Count != 1)
                throw new InvalidOperationException($"the Ratio cell '{cells[4]}' should hold one ratio");
            var ratio = decimal.Parse(RatioCell().Match(cells[4]).Groups[1].Value, CultureInfo.InvariantCulture);
            var js = Parse(Numbers().Match(cells[5]).Value);
            rows.Add(new FinalRow(NameCell().Match(cells[1]).Groups[1].Value, poly[0], poly[1], native, ratio, js));
        }
        return rows;
    }

    private static async Task AssertRow(
        IReadOnlyDictionary<(string, string), (int Avg, int Min)> csv,
        string benchmark, string language, int expectedAvg, int? expectedMin) {
        await Assert.That(csv.TryGetValue((benchmark, language), out var row)).IsTrue()
            .Because($"'{benchmark}' has no '{language}' row in {FinalRun}.csv");
        await Assert.That(expectedAvg).IsEqualTo(row.Avg)
            .Because($"{benchmark}/{language}: the document says {expectedAvg} us, the CSV says {row.Avg}");
        if (expectedMin is { } min)
            await Assert.That(min).IsEqualTo(row.Min)
                .Because($"{benchmark}/{language}: the document says min {min} us, the CSV says {row.Min}");
    }

    private static Dictionary<(string, string), (int Avg, int Min)> ReadCsv(string path) {
        var lines = File.ReadAllLines(path);
        var header = lines[0].Split(',');
        var benchmark = Array.IndexOf(header, "benchmark");
        var language = Array.IndexOf(header, "language");
        var avg = Array.IndexOf(header, "avg_us");
        var min = Array.IndexOf(header, "min_us");
        var rows = new Dictionary<(string, string), (int, int)>();
        foreach (var line in lines.Skip(1)) {
            var cells = line.Split(',');
            if (cells.Length <= Math.Max(Math.Max(benchmark, language), Math.Max(avg, min)))
                continue;
            rows[(cells[benchmark], cells[language])] = (
                Round(cells[avg]), Round(cells[min]));
        }
        return rows;
    }

    /// <summary>Poly / C# native to two decimals, from the same CSV values the table shows.</summary>
    private static decimal Ratio(int polyAvg, int nativeAvg) =>
        Math.Round((decimal)polyAvg / nativeAvg, 2, MidpointRounding.AwayFromZero);

    private static int Round(string value) =>
        (int)Math.Round(double.Parse(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);

    private static int Parse(string value) =>
        int.Parse(Commas().Replace(value, string.Empty), CultureInfo.InvariantCulture);

    // ── plumbing ─────────────────────────────────────────────────────────────

    private static string Document() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "docs", "perf-comparison", "README.md"));

    private static string ResultsDirectory() =>
        Path.Combine(RepoRoot(), "docs", "perf-comparison", "results");

    /// <summary>`nqueens 8` -> nqueens; keeps only the benchmark name in front of the size.</summary>
    [GeneratedRegex(@"^(nqueens|sieve|mandelbrot|collatz)\b")]
    private static partial Regex NameCell();

    [GeneratedRegex(@"\d[\d,]*(?:\.\d+)?")]
    private static partial Regex Numbers();

    [GeneratedRegex(",")]
    private static partial Regex Commas();

    [GeneratedRegex(@"\*{0,2}(\d+\.\d{2})x\*{0,2}")]
    private static partial Regex RatioCell();

    [GeneratedRegex(@"(?:results/)?(results_\d{8}_\d{6})\.csv")]
    private static partial Regex ResultsCsv();

    private static string RepoRoot() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Poly.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Poly.slnx not found above the test output directory");
    }
}