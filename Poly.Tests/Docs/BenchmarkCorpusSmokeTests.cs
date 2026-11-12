using System.Globalization;
using System.Text;

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
/// benchmark side by side. <c>Allocated</c> is runtime-stable and is the column to compare
/// against this file.
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
        await Assert.That(File.Exists(ParseBaseline.CsvPath)).IsTrue()
            .Because($"{ParseBaseline.CsvPath} is the committed parse baseline; the command that produces it is in Poly.Benchmarks/ParseBenchmarks.cs");

        var rows = ParseBaseline.Read();
        await Assert.That(rows.Count).IsGreaterThan(0)
            .Because("the baseline must record at least one row");

        var duplicates = rows
            .GroupBy(row => (row.Method, row.Entry))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Method} {group.Key.Entry}")
            .ToList();
        await Assert.That(duplicates).IsEmpty()
            .Because($"{ParseBaseline.CsvPath} repeats these rows");

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
        await Assert.That(File.Exists(ParseBaseline.CsvPath)).IsTrue()
            .Because($"{ParseBaseline.CsvPath} is the committed parse baseline; the command that produces it is in Poly.Benchmarks/ParseBenchmarks.cs");

        var rows = ParseBaseline.Read();
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

    /// <summary>The 62 <c>*.cs.golden</c> transcripts the C# aggregate row parses.</summary>
    internal static string CSharpGoldenDirectory =>
        Path.Combine(GoldenDomains.RepoRoot, "Poly.Tests", "DomainModeling", "Compile", "EmitGolden");
}

/// <summary>One data row of the committed parse baseline.</summary>
internal sealed record ParseBaselineRow(string Method, string Entry, double Mean, double Allocated);

/// <summary>
/// Reads <c>Poly.Benchmarks/baselines/parse.csv</c> by header name. The CSV is BenchmarkDotNet's
/// export with comma separators and invariant culture, so a quoted cell may contain a comma; the
/// parameter column's header is the benchmark parameter's name (<c>entry</c>), not <c>Params</c>.
/// </summary>
internal static class ParseBaseline {
    /// <summary>The committed baseline file.</summary>
    internal static string CsvPath =>
        Path.Combine(GoldenDomains.RepoRoot, "Poly.Benchmarks", "baselines", "parse.csv");

    /// <summary>Every data row, in file order.</summary>
    internal static IReadOnlyList<ParseBaselineRow> Read() {
        var lines = File.ReadAllLines(CsvPath);
        if (lines.Length < 2)
            throw new InvalidOperationException($"{CsvPath} has no data rows.");

        var header = SplitCsv(lines[0]);
        var method = HeaderIndex(header, "Method");
        var entry = HeaderIndex(header, "entry");
        var mean = HeaderIndex(header, "Mean");
        var allocated = HeaderIndex(header, "Allocated");

        var rows = new List<ParseBaselineRow>();
        foreach (var line in lines.Skip(1)) {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var cells = SplitCsv(line);
            rows.Add(new ParseBaselineRow(
                cells[method],
                cells[entry],
                Number(cells[mean], "Mean"),
                Number(cells[allocated], "Allocated")));
        }
        return rows;
    }

    private static int HeaderIndex(IReadOnlyList<string> header, string prefix) {
        for (var i = 0; i < header.Count; i++) {
            if (header[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        throw new InvalidOperationException(
            $"{CsvPath} has no '{prefix}' column; its header is: {string.Join(", ", header)}.");
    }

    private static double Number(string cell, string column) =>
        double.TryParse(
            cell.Trim(),
            NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : throw new InvalidOperationException($"{CsvPath}: column '{column}' is not a number: '{cell}'.");

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