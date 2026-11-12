using BenchmarkDotNet.Attributes;

using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;

namespace Poly.Benchmarks;

/// <summary>
/// The domain-analysis baseline: one row per document in <c>GoldenDomains.Compilable</c>, each
/// measuring the full analyze pipeline, <c>session.Analyze(domain)</c>, on a domain that already
/// loaded. The committed copy is <c>Poly.Benchmarks/baselines/analysis.csv</c>, produced by:
/// <code>
/// dotnet run -c Release --project Poly.Benchmarks -- --job short --filter '*AnalysisBenchmark*'
/// </code>
/// with BenchmarkDotNet 0.15.8, then copied verbatim from the exporter's report.
/// <para>
/// <b>Session.</b> The cases are built on <see cref="BenchmarkSupport.AuthoringSession"/>:
/// <c>DomainSession.ForAuthoringSource(text, [storage, persistence])</c>, the same
/// authoring pipeline <c>PolyCompiler</c> runs for the define gate (<c>apply_dsl</c> /
/// <c>validate_dsl</c>). It differs only in catalog — <see cref="ExtensionCatalog.Core"/> here,
/// <c>ProductCatalog.Model</c> there. That is analysis-neutral for these ten documents: none lists
/// a vendor <c>uses</c> id, so the vendor libraries contribute no analyzer, and the one library
/// Core adds (<c>tests</c>, the tests-artifact pass) contributes no error to any of them. Libraries
/// do contribute analyzers, which is why a core-only session without the authoring pack would not
/// be analysis-neutral.
/// </para>
/// <para>
/// <b>Usage rule.</b> Compare <c>Allocated</c> against the committed file — it is stable for a
/// given runtime — and re-run <c>Mean</c> side by side on one machine. The noise caveat covers every
/// row: <c>--job short</c> <c>Mean</c> is noisy, so no row is a threshold and a regression is a
/// side-by-side re-run, never a comparison against this file alone.
/// </para>
/// <para>
/// <b>After-number procedure.</b> Re-run the command above with the same <c>--job short</c> on the
/// same machine, copy the report over this file, then diff the two CSVs by <c>Allocated</c> per row
/// and re-run <c>Mean</c> for both revisions side by side.
/// </para>
/// <para>
/// <b>Base.</b> This file is a before-number taken at <c>0b40fc16</c> (the S-11 merge), on the
/// branch's base <c>origin/master</c>. It is a record, not a target: the analysis passes change
/// under other slices, so a later number compares against a re-run, never against this file.
/// </para>
/// </summary>
[Config(typeof(PolyBenchmarkConfig))]
[MemoryDiagnoser]
public class AnalysisBenchmark {
    /// <summary>The benchmark method name, pinned by the smoke test's row check.</summary>
    public const string Method = "Analysis_DomainGate";

    /// <summary>
    /// The ten documents this row covers: one copy of the golden catalog, tied back to
    /// <c>GoldenDomains.Compilable</c> by value in the smoke test.
    /// </summary>
    public static IReadOnlyList<string> Documents { get; } = [
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

    /// <summary>
    /// One case per document. The load, parse and evolution gate run here, not in a
    /// <c>[GlobalSetup]</c>, so <c>--list flat</c> enumerates every document and a broken one fails
    /// naming its path before any measurement.
    /// </summary>
    public static IEnumerable<AnalysisCase> Cases() => Documents.Select(Load);

    /// <summary>Analyzes one already-loaded golden domain through the session's full pipeline.</summary>
    [Benchmark]
    [ArgumentsSource(nameof(Cases))]
    public AnalysisResult Analysis_DomainGate(AnalysisCase entry) => entry.Session.Analyze(entry.Domain);

    /// <summary>Loads one document into a domain and a session, or throws naming the path.</summary>
    internal static AnalysisCase Load(string relativePath) {
        var name = Path.GetFileNameWithoutExtension(relativePath);
        var text = BenchmarkSupport.ReadDocument(relativePath);
        var session = BenchmarkSupport.AuthoringSession(text, relativePath);

        IReadOnlyList<DomainChange> changes;
        try {
            changes = new PolyDslParser(text, session).Parse();
        }
        catch (Exception exception) {
            throw new InvalidOperationException(
                $"Analysis corpus document '{relativePath}' does not parse: {exception.Message}", exception);
        }

        var result = new DomainEvolution(new Domain("_", [])).ApplyAndAnalyze(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Analysis corpus document '{relativePath}' does not analyze: {result.FailureSummary}");
        return new AnalysisCase(name, relativePath, session, result.Root);
    }
}

/// <summary>
/// One analysis case: a golden document's loaded domain and the session the define gate would use.
/// <see cref="ToString"/> is the CSV parameter cell and the baseline's key — <c>analysis/&lt;name&gt;</c>.
/// </summary>
public sealed record AnalysisCase(string Name, string Path, DomainSession Session, Domain Domain) {
    public override string ToString() => $"analysis/{Name}";
}