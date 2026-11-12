using System.Globalization;

using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Reports;

using Perfolizer.Horology;
using Perfolizer.Metrology;

namespace Poly.Benchmarks;

/// <summary>
/// Pins the CSV export every committed baseline under <c>Poly.Benchmarks/baselines/</c> is copied
/// from: comma-separated invariant culture, units in the header, bytes and nanoseconds, and a
/// 40-character parameter column so no case key is trimmed (BenchmarkDotNet trims past 20
/// otherwise). The parse, invoke, analysis and simulator rows share this one config, so one reader
/// serves all four files.
/// </summary>
public sealed class PolyBenchmarkConfig : ManualConfig {
    /// <summary>The style the console table and the exported CSV columns share.</summary>
    public static SummaryStyle Style { get; } = new(
        cultureInfo: CultureInfo.InvariantCulture,
        printUnitsInHeader: true,
        sizeUnit: SizeUnit.B,
        timeUnit: TimeUnit.Nanosecond,
        printUnitsInContent: false,
        maxParameterColumnWidth: 40);

    public PolyBenchmarkConfig() {
        AddExporter(new CsvExporter(CsvSeparator.Comma, Style));
        HideColumns(Column.Job, Column.Runtime, Column.EnvironmentVariables);
        WithSummaryStyle(Style);
    }
}