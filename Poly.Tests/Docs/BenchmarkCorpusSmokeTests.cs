using System.Globalization;
using System.Text;

using Poly.Benchmarks;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Docs;

/// <summary>
/// Guards the committed parse baseline, <c>Poly.Benchmarks/baselines/parse.csv</c>: the CSV
/// exporter's report from one <c>--job short</c> run of <c>Poly.Benchmarks.ParseBenchmarks</c>,
/// copied verbatim. The benchmark cannot read the golden catalog (it lives in the test project),
/// so the baseline is the tie: its <c>dsl/</c> rows must be exactly
/// <see cref="GoldenDomains.Compilable"/>, and its C# aggregate row must name a live corpus.
/// <para>
/// The baseline pins schema and coverage, never a number: <c>Mean</c> is machine-dependent, so
/// <c>Mean &gt; 0</c> is a schema check only, and a regression is judged by re-running the
/// benchmark side by side. <c>Allocated</c> is stable for a given runtime and is the column to
/// compare against this file. The noise caveat covers every row: <c>--job short</c> <c>Mean</c> is
/// noisy on all of them (the plan-review note records ~49% on the university row and ~90% on the C#
/// aggregate), so no row is a threshold.
/// </para>
/// </summary>
public sealed class BenchmarkCorpusSmokeTests {
    /// <summary>The benchmark that records one row per DSL document.</summary>
    internal const string DslMethod = "Parse_DslCorpus";

    /// <summary>The benchmark that records the single C# aggregate row.</summary>
    internal const string CSharpMethod = "Parse_CSharpCorpus";

    /// <summary>The C# aggregate key; it carries no file count, so adding a golden cannot age the baseline.</summary>
    internal const string CSharpKey = "csharp/golden";

    [Test]
    public async Task Baseline_FileExists_AndEveryRowNamesALiveDocument() {
        await Assert.That(File.Exists(BenchmarkBaseline.ParseCsvPath)).IsTrue()
            .Because($"{BenchmarkBaseline.ParseCsvPath} is the committed parse baseline; the command that produces it is in Poly.Benchmarks/ParseBenchmarks.cs");

        var rows = BenchmarkBaseline.Read(BenchmarkBaseline.ParseCsvPath);

        var duplicates = rows
            .GroupBy(row => (row.Method, row.Entry))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Method} {group.Key.Entry}")
            .ToList();
        await Assert.That(duplicates).IsEmpty()
            .Because($"{BenchmarkBaseline.ParseCsvPath} repeats these rows");

        var goldenFiles = Directory.Exists(CSharpGoldenDirectory)
            ? Directory.GetFiles(CSharpGoldenDirectory, "*.cs.golden", SearchOption.AllDirectories)
            : [];
        var broken = new List<string>();
        foreach (var row in rows) {
            if (row.Method == DslMethod) {
                if (!row.Entry.StartsWith("dsl/", StringComparison.Ordinal)) {
                    broken.Add($"{row.Entry} is not a dsl/ key");
                    continue;
                }
                var name = row.Entry["dsl/".Length..];
                var domain = GoldenDomains.Compilable.FirstOrDefault(candidate => candidate.Name == name);
                if (domain is null)
                    broken.Add($"{row.Entry} names no document in GoldenDomains.Compilable");
                else if (!File.Exists(domain.FullPath))
                    broken.Add($"{row.Entry} names {domain.Path}, which is missing");
            }
            else if (row.Method == CSharpMethod) {
                if (row.Entry != CSharpKey)
                    broken.Add($"{row.Method} has key '{row.Entry}', expected '{CSharpKey}'");
                else if (goldenFiles.Length == 0)
                    broken.Add($"{CSharpKey} names no .cs.golden file under {CSharpGoldenDirectory}");
            }
            else {
                broken.Add($"{row.Method} is not one of the pinned benchmark methods");
            }
        }
        await Assert.That(broken).IsEmpty()
            .Because($"every baseline row must name a live corpus entry: {string.Join("; ", broken)}");

        var outOfRange = rows
            .Where(row => row.Mean <= 0 || row.Allocated < 0)
            .Select(row => $"{row.Method} {row.Entry}: Mean={row.Mean}, Allocated={row.Allocated}")
            .ToList();
        await Assert.That(outOfRange).IsEmpty()
            .Because("Mean must be positive and Allocated non-negative — a schema check, not a threshold");
    }

    [Test]
    public async Task BenchmarkBaseline_DslRowsAreExactlyTheCompilableSet_AndTheCSharpAggregateRowExists() {
        await Assert.That(File.Exists(BenchmarkBaseline.ParseCsvPath)).IsTrue()
            .Because($"{BenchmarkBaseline.ParseCsvPath} is the committed parse baseline; the command that produces it is in Poly.Benchmarks/ParseBenchmarks.cs");

        var rows = BenchmarkBaseline.Read(BenchmarkBaseline.ParseCsvPath);
        var recorded = rows
            .Where(row => row.Method == DslMethod)
            .Select(row => row.Entry)
            .Order(StringComparer.Ordinal)
            .ToList();
        var expected = GoldenDomains.Compilable
            .Select(domain => "dsl/" + domain.Name)
            .Order(StringComparer.Ordinal)
            .ToList();
        await Assert.That(string.Join(", ", recorded)).IsEqualTo(string.Join(", ", expected))
            .Because("the DSL rows must be exactly GoldenDomains.Compilable, so a moved golden cannot leave the baseline behind");

        await Assert.That(rows.Any(row => row.Method == CSharpMethod && row.Entry == CSharpKey)).IsTrue()
            .Because($"the C# aggregate row '{CSharpKey}' must be present");
    }

    /// <summary>
    /// A3: the shared loader's contract is an <see cref="InvalidOperationException"/> naming the
    /// document's full path, so an unreadable corpus member names itself instead of surfacing the
    /// OS exception type. <c>chmod 000</c> is not enforced on Windows, and a privileged process
    /// ignores it, so both skip the test.
    /// </summary>
    [Test]
    public async Task BenchmarkSupport_ReadText_UnreadableDocument_ThrowsWithThePath() {
        Skip.When(
            OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess,
            "chmod 000 is not enforced on Windows, and a privileged process ignores it");

        var directory = Path.Combine(Path.GetTempPath(), "poly-benchmark-readtext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var fullPath = Path.Combine(directory, "unreadable.poly");
        await File.WriteAllTextAsync(fullPath, "domain Unreadable\n");
        try {
            // The guard is for the platform analyzer; Skip.When above decides whether the test runs.
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(fullPath, UnixFileMode.None);
            var error = Assert.Throws<InvalidOperationException>(
                () => BenchmarkSupport.ReadText("corpus/unreadable.poly", fullPath));
            await Assert.That(error.Message).Contains(fullPath)
                .Because("an unreadable corpus document must name its full path, not surface an OS exception");
        }
        finally {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(fullPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The parse tie: the benchmark's own document list is exactly <see cref="GoldenDomains.Compilable"/>.
    /// The benchmark cannot read the golden catalog (it lives in the test project), so this is the
    /// typed tie — a moved golden fails here, not only in the CSV row check.
    /// </summary>
    [Test]
    public async Task ParseCorpus_TiesTheBenchmarkDocumentListToTheGoldenCatalog() {
        var recorded = ParseBenchmarks.DslPaths.Order(StringComparer.Ordinal).ToList();
        var expected = GoldenDomains.Compilable
            .Select(domain => domain.Path)
            .Order(StringComparer.Ordinal)
            .ToList();
        await Assert.That(string.Join(", ", recorded)).IsEqualTo(string.Join(", ", expected))
            .Because("the parse benchmark's document list must be exactly GoldenDomains.Compilable");
    }

    /// <summary>The 62 <c>*.cs.golden</c> transcripts the C# aggregate row parses.</summary>
    internal static string CSharpGoldenDirectory =>
        Path.Combine(GoldenDomains.RepoRoot, "Poly.Tests", "DomainModeling", "Compile", "EmitGolden");

    /// <summary>
    /// The reader fails closed on a header that loses a pinned unit and on a row that does not
    /// match the header's width, naming the file it read and — for a row — the line it came from.
    /// </summary>
    [Test]
    public async Task BenchmarkBaseline_HostileCsv_ThrowsWithTheFileAndTheRow() {
        var directory = Path.Combine(Path.GetTempPath(), "poly-benchmark-csv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var unitless = Path.Combine(directory, "unitless-mean.csv");
            await File.WriteAllTextAsync(unitless,
                "Method,entry,Mean,Allocated [B]\nParse_DslCorpus,dsl/hotel,1,2\n");
            var unitError = Assert.Throws<InvalidOperationException>(() => BenchmarkBaseline.Read(unitless));
            await Assert.That(unitError.Message).Contains(unitless)
                .Because("a baseline that loses a pinned unit must fail closed naming the file");
            await Assert.That(unitError.Message).Contains("Mean [ns]")
                .Because("the error must name the header the reader requires");

            var ragged = Path.Combine(directory, "ragged.csv");
            await File.WriteAllTextAsync(ragged,
                "Method,entry,Mean [ns],Allocated [B]\nParse_DslCorpus,dsl/hotel,1\n");
            var rowError = Assert.Throws<InvalidOperationException>(() => BenchmarkBaseline.Read(ragged));
            await Assert.That(rowError.Message).Contains(ragged)
                .Because("a ragged row must name the file it came from");
            await Assert.That(rowError.Message).Contains("data line 1")
                .Because("a ragged row must name the 1-based data line");
        }
        finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The reader fails closed on a baseline it cannot open, and on one that carries a header but no
    /// data row — a refresh that wrote only blank lines is not an empty baseline, it is a broken one.
    /// </summary>
    [Test]
    public async Task BenchmarkBaseline_MissingFileAndEmptyBody_ThrowWithThePath() {
        var directory = Path.Combine(Path.GetTempPath(), "poly-benchmark-csv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var missing = Path.Combine(directory, "missing.csv");
            var missingError = Assert.Throws<InvalidOperationException>(() => BenchmarkBaseline.Read(missing));
            await Assert.That(missingError.Message).Contains(missing)
                .Because("a missing baseline must name the path it could not read");

            var blank = Path.Combine(directory, "blank.csv");
            await File.WriteAllTextAsync(blank,
                "Method,entry,Mean [ns],Allocated [B]\n\n\n");
            var blankError = Assert.Throws<InvalidOperationException>(() => BenchmarkBaseline.Read(blank));
            await Assert.That(blankError.Message).Contains(blank)
                .Because("a header with no data rows must name the file");
        }
        finally {
            Directory.Delete(directory, recursive: true);
        }
    }
}

/// <summary>One data row of a committed benchmark baseline.</summary>
internal sealed record BenchmarkBaselineRow(string Method, string Entry, double Mean, double Allocated);

/// <summary>
/// Reads a committed baseline under <c>Poly.Benchmarks/baselines/</c> by exact header name. The CSV
/// is BenchmarkDotNet's export with comma separators and invariant culture, so a quoted cell may
/// contain a comma; the parameter column's header is the benchmark parameter's name
/// (<c>entry</c>), not <c>Params</c>.
/// <para>
/// Every failure names the file it read, and a data-row failure also names the row, so a refresh
/// that loses a pinned unit or drops a cell fails closed with the offending line.
/// </para>
/// </summary>
internal static class BenchmarkBaseline {
    /// <summary>The committed parse baseline.</summary>
    internal static string ParseCsvPath => CsvPath("parse.csv");

    /// <summary>The committed two-phase invoke baseline.</summary>
    internal static string InvokeCsvPath => CsvPath("invoke.csv");

    /// <summary>The committed domain-analysis baseline.</summary>
    internal static string AnalysisCsvPath => CsvPath("analysis.csv");

    /// <summary>The committed simulator invoke baseline.</summary>
    internal static string SimulatorCsvPath => CsvPath("simulator.csv");

    /// <summary>
    /// The columns every baseline must carry, by exact header. A refresh that renames a unit
    /// (<c>Mean</c> without <c>[ns]</c>, <c>Allocated</c> without <c>[B]</c>) would otherwise be
    /// compared in the wrong unit, so the reader refuses it.
    /// </summary>
    internal static readonly string[] RequiredHeaders = ["Method", "entry", "Mean [ns]", "Allocated [B]"];

    private static string CsvPath(string name) =>
        Path.Combine(GoldenDomains.RepoRoot, "Poly.Benchmarks", "baselines", name);

    /// <summary>Every data row of <paramref name="csvPath"/>, in file order.</summary>
    internal static IReadOnlyList<BenchmarkBaselineRow> Read(string csvPath) {
        string[] lines;
        try {
            lines = File.ReadAllLines(csvPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            throw new InvalidOperationException(
                $"Baseline '{csvPath}' cannot be read: {exception.Message}", exception);
        }
        if (lines.Length < 2)
            throw new InvalidOperationException($"{csvPath} has no data rows.");

        var header = SplitCsv(lines[0]);
        var method = HeaderIndex(header, "Method", csvPath);
        var entry = HeaderIndex(header, "entry", csvPath);
        var mean = HeaderIndex(header, "Mean [ns]", csvPath);
        var allocated = HeaderIndex(header, "Allocated [B]", csvPath);

        var rows = new List<BenchmarkBaselineRow>();
        for (var i = 1; i < lines.Length; i++) {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;
            var cells = SplitCsv(lines[i]);
            if (cells.Length != header.Length)
                throw new InvalidOperationException(
                    $"{csvPath} data line {i} (file line {i + 1}) has {cells.Length} cells; the header has {header.Length}.");
            rows.Add(new BenchmarkBaselineRow(
                cells[method],
                cells[entry],
                Number(cells[mean], "Mean", csvPath, i),
                Number(cells[allocated], "Allocated", csvPath, i)));
        }
        if (rows.Count == 0)
            throw new InvalidOperationException($"{csvPath} has a header but no data rows.");
        return rows;
    }

    private static int HeaderIndex(IReadOnlyList<string> header, string name, string csvPath) {
        for (var i = 0; i < header.Count; i++) {
            if (string.Equals(header[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        throw new InvalidOperationException(
            $"{csvPath} has no '{name}' column; its header is: {string.Join(", ", header)}.");
    }

    private static double Number(string cell, string column, string csvPath, int line) =>
        double.TryParse(
            cell.Trim(),
            NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : throw new InvalidOperationException(
                $"{csvPath} data line {line} (file line {line + 1}): column '{column}' is not a number: '{cell}'.");

    private static string[] SplitCsv(string line) {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++) {
            var c = line[i];
            if (quoted) {
                if (c != '"') {
                    cell.Append(c);
                }
                else if (i + 1 < line.Length && line[i + 1] == '"') {
                    cell.Append('"');
                    i++;
                }
                else {
                    quoted = false;
                }
            }
            else if (c == '"') {
                quoted = true;
            }
            else if (c == ',') {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else {
                cell.Append(c);
            }
        }
        cells.Add(cell.ToString());
        return [.. cells];
    }
}