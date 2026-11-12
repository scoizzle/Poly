using System.Runtime.CompilerServices;

using BenchmarkDotNet.Attributes;

using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Language;
using Poly.Interpretation.CSharp;

namespace Poly.Benchmarks;

/// <summary>
/// The parse baseline: one <c>Allocated</c> and <c>Mean</c> row per DSL document in
/// <c>GoldenDomains.Compilable</c> and one aggregate row for the 62 <c>*.cs.golden</c>
/// transcripts under <c>Poly.Tests/DomainModeling/Compile/EmitGolden</c>. The committed copy is
/// <c>Poly.Benchmarks/baselines/parse.csv</c>, produced by:
/// <code>
/// dotnet run -c Release --project Poly.Benchmarks -- --filter '*Parse*' --job short
/// </code>
/// with BenchmarkDotNet 0.15.8, then copied verbatim from the exporter's report. <c>--job short</c>
/// is only comparable with another short run on the same machine.
/// <para>
/// Usage rule: compare <c>Allocated</c> against the committed file — it is stable for a given
/// runtime — and re-run <c>Mean</c> side by side on one machine, because the committed value carries
/// no host or CPU information. The noise caveat covers every row: <c>--job short</c> <c>Mean</c> is a
/// noisy statistic (the plan-review note records ~49% on the university row and ~90% on the C#
/// aggregate), so only <c>Allocated</c> is comparable across runs. <c>Mean &gt; 0</c> in the smoke
/// test is a schema check, never a threshold.
/// </para>
/// <para>
/// The measured DSL op is the real parse, <c>new PolyDslParser(text, session).Parse()</c>, against
/// a session from <c>DomainSession.ForSource(text, ExtensionCatalog.ProductAuthoring)</c> (seed
/// <c>["storage"]</c>, core libraries only). That differs from Q-01's session factory, which adds
/// <c>SqliteLibrary</c>, <c>HttpLibrary</c> and the <c>sqlite</c> seed; the difference is
/// parse-neutral because none of these documents selects a vendor library.
/// </para>
/// <para>
/// The corpus root is a <see cref="CallerFilePathAttribute"/> constant in
/// <see cref="BenchmarkSupport"/>, so a baseline refresh must run from the checkout that built the
/// benchmark: BenchmarkDotNet's child process runs from
/// <c>BenchmarkDotNet.Artifacts/&lt;guid&gt;/</c>, not the repository.
/// </para>
/// </summary>
[Config(typeof(PolyBenchmarkConfig))]
[MemoryDiagnoser]
public class ParseBenchmarks {
    /// <summary>
    /// The only list of the 10 DSL documents in this project. It is a third copy of the catalog
    /// (after <c>GoldenDomains</c> and its test), tied back to the golden set by the test that reads
    /// this run's CSV and by the smoke test that compares it to <c>GoldenDomains.Compilable</c>.
    /// </summary>
    public static IReadOnlyList<string> DslPaths { get; } = [
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
    /// One case per DSL document. The per-document work lives here, not in a
    /// <c>[GlobalSetup]</c>: a setup method cannot see an <c>[ArgumentsSource]</c> value. Every
    /// failure names the path, and the host enumerates this source on <c>--list</c>, so a broken
    /// document fails before any measurement.
    /// </summary>
    public static IEnumerable<ParseCorpusEntry> DslCorpus() => DslPaths.Select(LoadDsl);

    /// <summary>The single aggregate C# case.</summary>
    public static IEnumerable<ParseCorpusEntry> CSharpCorpus() => [LoadCSharp()];

    /// <summary>Tokenizes and resolves one document.</summary>
    [Benchmark]
    [ArgumentsSource(nameof(DslCorpus))]
    public int Parse_DslCorpus(ParseCorpusEntry entry) =>
        new PolyDslParser(entry.Documents[0], SessionOf(entry)).Parse().Count;

    /// <summary>Reads every golden transcript; the node count keeps the reads live.</summary>
    [Benchmark]
    [ArgumentsSource(nameof(CSharpCorpus))]
    public int Parse_CSharpCorpus(ParseCorpusEntry entry) {
        var nodes = 0;
        foreach (var text in entry.Documents)
            nodes += CSharpLanguage.Read(text).Children.Count();
        return nodes;
    }

    private static DomainSession SessionOf(ParseCorpusEntry entry) =>
        entry.Session ?? throw new InvalidOperationException($"{entry} has no session; only DSL entries carry one.");

    private static ParseCorpusEntry LoadDsl(string relativePath) {
        var fullPath = Path.Combine(BenchmarkSupport.RepoRoot(), relativePath);
        var text = BenchmarkSupport.ReadText(relativePath, fullPath);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"Parse corpus document '{relativePath}' is empty ({fullPath}).");

        DomainSession session;
        try {
            session = DomainSession.ForSource(text, ExtensionCatalog.ProductAuthoring);
            _ = new PolyDslParser(text, session).Parse();
        }
        catch (Exception exception) {
            throw new InvalidOperationException(
                $"Parse corpus document '{relativePath}' does not parse ({fullPath}): {exception.Message}", exception);
        }

        return new ParseCorpusEntry("dsl", Path.GetFileNameWithoutExtension(relativePath), [text], session);
    }

    private static ParseCorpusEntry LoadCSharp() {
        var root = Path.Combine(BenchmarkSupport.RepoRoot(), "Poly.Tests", "DomainModeling", "Compile", "EmitGolden");
        if (!Directory.Exists(root))
            throw new InvalidOperationException($"C# golden corpus directory is missing ({root}).");
        var paths = Directory
            .GetFiles(root, "*.cs.golden", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (paths.Length == 0)
            throw new InvalidOperationException($"C# golden corpus is empty ({root}).");

        var documents = new string[paths.Length];
        for (var i = 0; i < paths.Length; i++) {
            var relativePath = Path.GetRelativePath(BenchmarkSupport.RepoRoot(), paths[i]);
            documents[i] = BenchmarkSupport.ReadText(relativePath, paths[i]);
            if (string.IsNullOrWhiteSpace(documents[i]))
                throw new InvalidOperationException($"C# golden '{relativePath}' is empty ({paths[i]}).");
            try {
                _ = CSharpLanguage.Read(documents[i]);
            }
            catch (Exception exception) {
                throw new InvalidOperationException(
                    $"C# golden '{relativePath}' does not read ({paths[i]}): {exception.Message}", exception);
            }
        }

        return new ParseCorpusEntry("csharp", "golden", documents, Session: null);
    }

}

/// <summary>
/// One benchmark case: a corpus member's documents, plus the session a DSL case parses with.
/// <see cref="ToString"/> is the CSV parameter cell and the baseline's key — never the text.
/// </summary>
public sealed record ParseCorpusEntry(
    string Corpus,
    string Name,
    IReadOnlyList<string> Documents,
    DomainSession? Session) {
    public override string ToString() => $"{Corpus}/{Name}";
}