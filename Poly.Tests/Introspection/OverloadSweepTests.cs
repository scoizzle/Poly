using System.Text;

using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Http;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Packs.Sqlite;
using Poly.Tests.Docs;
using Poly.Tests.Interpretation;

namespace Poly.Tests.Introspection;

/// <summary>
/// T-16 amendment 1: which overload every call site binds, for every shipped document and for the C# reader
/// corpus. The instrument is <see cref="CallSiteCatalogMetadata"/>: one identity
/// (<c>Ns.Type.Method(ParamType1,ParamType2)</c>) per Invoke, New and getter site. The emitted C# cannot show a
/// winner change, because the C# compiler rebinds. <c>OverloadSweepBaseline.txt</c> was captured from origin/master
/// (0f79ba45, in a scratch worktree, with this file) before any production edit; a difference is a defect or a row in
/// <see cref="ExplainedDeltas"/> and the PR, never a new baseline. Entries are keyed by document path, wrapper id, guide
/// block first line (with an ordinal) or C# source path plus ordinal within that source, never by a running index, and the
/// comparison is per baseline entry: a missing or changed entry fails, an entry the baseline lacks is listed in the test
/// output and does not fail. A later slice that legitimately changes a winner adds a row to <see cref="ExplainedDeltas"/>.
/// The first capture (master 072d8803) also swept the printed
/// goldens; one of them analyzed or threw depending on the run's test order, so they were dropped and the baseline was
/// captured again from master, with no production change in between. The keys were later changed from running indexes to
/// path plus ordinal as a pure re-key: every body is byte-identical. Blind spots: a per-unit set of identities (a swap at two
/// sites could cancel), nothing recorded for unresolved or modeled-method sites, and 29 of 100 documents record no identity.
/// Set <c>POLY_SWEEP_OUT</c> to a path to write the current sweep there instead of comparing.
/// </summary>
public class OverloadSweepTests {
    private static readonly ExtensionCatalog Catalog = ExtensionCatalog.Core
        .With(new SqliteLibrary())
        .With(new HttpLibrary());

    private static readonly string[] Seed = [.. ExtensionCatalog.ProductAuthoring, "sqlite"];

    private static readonly string[] SkippedDirectories = ["bin", "obj", ".git", ".t3", "node_modules", ".vs", "artifacts"];

    /// <summary>
    /// Every difference from the baseline a slice causes, keyed by the entry it changes: the body text it replaces (found
    /// exactly once in that entry) and what stands there now. The baseline file is never rewritten for a behaviour change;
    /// <b>a later slice that legitimately changes a winner adds a row here and explains it in its PR</b>, as T-16 does for
    /// <c>PopCount(1)</c>. Each row is pinned in a test of its own.
    /// </summary>
    private static readonly (string Key, string Before, string After, string Why)[] ExplainedDeltas = [
        ("csharp Poly.Tests/Interpretation/CSharpQualifiedNameTests.cs #4",
         "  errors: 1\n  System.Numerics.BitOperations.PopCount(System.UInt32)\n",
         "  errors: 2\n",
         "C# reader corpus, not the DSL: PopCount(1) bound PopCount(uint) through the removed Int32 -> UInt32 widening; "
         + "C# 10.2.11 allows it for a constant, Poly does not model constant-expression conversions, so no overload is left"),
        ("poly file demo/Poly.RestApi/library.poly",
         "  Poly.Runtime.DomainResult.get_IsSuccess()\n  Poly.Runtime.DomainResult`1[[System.Object, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]].get_ErrorMessage()\n  Poly.DomainModeling.Runtime.ConstraintFailureException.ctor(System.String)\n",
         "  Poly.Runtime.DomainResult.get_IsSuccess()\n",
         L04aWhy),
        ("poly file docs/missions/evolve-2026-10-13/saas.poly",
         "  Poly.Runtime.DomainResult.Success()\n",
         "  Poly.Runtime.DomainResult.Success()\n  Poly.Runtime.DomainResult.get_Error()\n",
         L04aWhy),
        ("poly file docs/missions/evolve-2026-10-14/jobqueue.poly",
         "  System.String.IsNullOrEmpty(System.String)\n  Poly.DomainModeling.Runtime.ConstraintFailureException.ctor(System.String)\n",
         "  System.String.IsNullOrEmpty(System.String)\n",
         L04aWhy),
        ("poly file docs/missions/evolve-2026-10-14/notifications.poly",
         "  System.String.IsNullOrEmpty(System.String)\n  Poly.DomainModeling.Runtime.ConstraintFailureException.ctor(System.String)\n",
         "  System.String.IsNullOrEmpty(System.String)\n",
         L04aWhy),
        ("poly file docs/probes/dogfood/entry-transition-in-first-stage.poly",
         "  errors: 0\n  System.String.IsNullOrEmpty(System.String)\n  Poly.Runtime.DomainFailure.Required(System.String,System.String,System.String)\n  Poly.Runtime.DomainResult`1[[System.Object, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]].Failure(Poly.Runtime.DomainFailure)\n",
         "  errors: 0\n  Poly.Runtime.DomainResult.Success()\n  System.String.IsNullOrEmpty(System.String)\n  Poly.Runtime.DomainFailure.Required(System.String,System.String,System.String)\n  Poly.Runtime.DomainResult`1[[System.Object, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]].Failure(Poly.Runtime.DomainFailure)\n  Poly.Runtime.DomainResult.get_IsSuccess()\n  Poly.Runtime.DomainResult.get_Error()\n",
         L04aWhy),
        ("poly file docs/probes/fleet-eval/12-mcp/mcp-library.poly",
         "  Poly.Runtime.DomainFailure.At(System.String)\n  Poly.Runtime.DomainResult`1[[System.Object, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]].get_ErrorMessage()\n  Poly.DomainModeling.Runtime.ConstraintFailureException.ctor(System.String)\n  Poly.Runtime.DomainResult.Success()\n  System.String.IsNullOrEmpty(System.String)\n  Poly.Runtime.DomainFailure.Required(System.String,System.String,System.String)\n  System.Collections.IList.Clear()\n  Poly.Runtime.DomainFailure.Range(System.String,System.String,System.String)\n  Poly.Runtime.DomainFailure.Unique(System.String,System.String,System.String)\n  Poly.Runtime.IStoragePort.Record(System.String,System.String,System.String)\n  System.Object.Equals(System.Object,System.Object)\n  Poly.Runtime.DomainFailure.Constraint(System.String,System.String,System.String)\n  Poly.Runtime.DomainResult.Failure(Poly.Runtime.DomainFailure)\n",
         "  Poly.Runtime.DomainFailure.At(System.String)\n  Poly.Runtime.DomainResult.Failure(Poly.Runtime.DomainFailure)\n  Poly.Runtime.DomainResult.Success()\n  System.String.IsNullOrEmpty(System.String)\n  Poly.Runtime.DomainFailure.Required(System.String,System.String,System.String)\n  System.Collections.IList.Clear()\n  Poly.Runtime.DomainFailure.Range(System.String,System.String,System.String)\n  Poly.Runtime.DomainFailure.Unique(System.String,System.String,System.String)\n  Poly.Runtime.IStoragePort.Record(System.String,System.String,System.String)\n  System.Object.Equals(System.Object,System.Object)\n  Poly.Runtime.DomainFailure.Constraint(System.String,System.String,System.String)\n",
         L04aWhy),
        ("poly file docs/probes/grep-2026-10-17/grep.poly",
         "  System.Object.ReferenceEquals(System.Object,System.Object)\n  Poly.Runtime.DomainResult`1[[System.Object, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]].get_ErrorMessage()\n  Poly.DomainModeling.Runtime.ConstraintFailureException.ctor(System.String)\n",
         "  System.Object.ReferenceEquals(System.Object,System.Object)\n",
         L04aWhy),
        ("poly file docs/probes/scratch-2026-10-14/payroll.poly",
         "  System.DateOnly.AddDays(System.Int32)\n  Poly.DomainModeling.Runtime.ConstraintFailureException.ctor(System.String)\n",
         "  System.DateOnly.AddDays(System.Int32)\n",
         L04aWhy),
        ("poly file docs/probes/scratch-2026-10-14/roastery.poly",
         "  Poly.Runtime.DomainResult.Success()\n  Poly.DomainModeling.Runtime.ConstraintFailureException.ctor(System.String)\n  System.String.IsNullOrEmpty(System.String)\n  Poly.Runtime.DomainFailure.Required(System.String,System.String,System.String)\n  System.String.get_Length()\n  Poly.Runtime.DomainFailure.Length(System.String,System.String,System.String)\n  System.String.Concat(System.Object,System.Object,System.Object)\n  Poly.Runtime.DomainFailure.Enum(System.String,System.String,System.String)\n  Poly.Runtime.DomainFailure.Range(System.String,System.String,System.String)\n",
         "  Poly.Runtime.DomainResult.Success()\n  Poly.Runtime.DomainFailure.Range(System.String,System.String,System.String)\n  System.String.IsNullOrEmpty(System.String)\n  Poly.Runtime.DomainFailure.Required(System.String,System.String,System.String)\n  System.String.get_Length()\n  Poly.Runtime.DomainFailure.Length(System.String,System.String,System.String)\n  System.String.Concat(System.Object,System.Object,System.Object)\n  Poly.Runtime.DomainFailure.Enum(System.String,System.String,System.String)\n",
         L04aWhy),
        ("poly guide domain Orders #0",
         "  Poly.Runtime.DomainResult`1[[System.Object, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]].Failure(Poly.Runtime.DomainFailure)\n",
         "  Poly.Runtime.DomainResult`1[[System.Object, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]].Failure(Poly.Runtime.DomainFailure)\n  Poly.Runtime.DomainResult.get_Error()\n",
         L04aWhy),
    ];

    /// <summary>
    /// L-04a: the initial stage's entry and every subscription handler RETURN a failure where they threw one. The
    /// <c>ConstraintFailureException</c> constructor and <c>ErrorMessage</c> sites leave these units; <c>Failure</c>, <c>Error</c>,
    /// <c>IsSuccess</c> and <c>Success</c> sites arrive (the entry now returns a result and Create propagates it), and a
    /// first occurrence can move. Each affected unit's identity set changes by exactly those lines; no call site rebinds
    /// to another overload of the same method.
    /// </summary>
    private const string L04aWhy = "L-04a: a failure that was thrown is returned (ConstraintFailureException.ctor and ErrorMessage out; "
        + "DomainResult.Failure, Error, IsSuccess and Success in), no overload winner changes";

    /// <summary>
    /// Identities a slice adds at new call sites, where nothing rebinds: each method has exactly one overload, so the
    /// line can only appear, never change winner. The run drops these lines before comparing, so every baseline entry is
    /// still compared on what it had; a changed winner anywhere still fails. Each row names its slice and is pinned below.
    /// </summary>
    private static readonly (string Identity, string Why)[] ExplainedAdditions = [
        ("Poly.Runtime.ActionScope.Touch(System.Object)",
         "L-03c: every generated mutator (action wrapper, handler, Attach*, Register*Subscriber) touches its receiver at entry"),
        ("Poly.Runtime.ActionScope.Create(System.Object,System.String)",
         "L-03c: every generated factory records the instance it created with the open action scope"),
    ];

    /// <summary>
    /// Entries whose error count depends on what the run has loaded, compared on their identities only. T-22a: the C# reader
    /// unit <c>(Customer)(-c)</c> names a type <c>Customer</c> no assembly of the test project declares; the registry's bare-name
    /// lookup resolves it to a class another test compiled and loaded earlier in the run (so <c>-c</c> is undefined, one error,
    /// as in C#), and to nothing when the unit runs alone (no type, not judged, no error). The unit has no call site.
    /// </summary>
    private static readonly (string Key, string Why)[] RunDependentErrorCounts = [
        ("csharp Poly.Tests/Interpretation/CSharpReaderTests.cs #10",
         "T-22a judges unary minus by the provider; 'Customer' resolves only when an earlier test loaded a type of that name"),
    ];

    private static string WithoutRunDependentErrorCount(string key, string body) =>
        RunDependentErrorCounts.Any(r => r.Key == key)
            ? string.Concat(body.Split('\n').Where(static l => l.Length > 0 && !l.StartsWith("  errors:", StringComparison.Ordinal)).Select(static l => l + "\n"))
            : body;

    private static string WithoutExplainedAdditions(string body) =>
        string.Concat(body.Split('\n')
            .Where(line => !ExplainedAdditions.Any(a => line == "  " + a.Identity))
            .Select(line => line + "\n"))[..^1];

    private const string GoldenPrefix = "Poly.Tests/DomainModeling/Compile/EmitGolden/";

    private static string BaselinePath() =>
        Path.Combine(GoldenDomains.RepoRoot, "Poly.Tests", "Introspection", "OverloadSweepBaseline.txt");

    /// <summary>The outcome of comparing a run to the baseline, entry by entry.</summary>
    internal sealed record SweepComparison(IReadOnlyList<string> Changed, IReadOnlyList<string> Missing, IReadOnlyList<string> Added, int Matched);

    /// <summary>
    /// A baseline entry missing from the run, or whose text changed, is a failure. An entry the baseline does not have
    /// (a new document, guide block or corpus unit) is listed, not a failure: it has no earlier binding to compare to.
    /// </summary>
    internal static SweepComparison Compare(
        IReadOnlyList<(string Key, string Body)> baseline, IReadOnlyList<(string Key, string Body)> run) {
        var runByKey = run.ToDictionary(static e => e.Key, static e => e.Body, StringComparer.Ordinal);
        var baselineKeys = baseline.Select(static e => e.Key).ToHashSet(StringComparer.Ordinal);
        var changed = new List<string>();
        var missing = new List<string>();
        var matched = 0;
        foreach (var (key, body) in baseline) {
            if (!runByKey.TryGetValue(key, out var actual))
                missing.Add(key);
            else if (!string.Equals(actual, body, StringComparison.Ordinal))
                changed.Add(key);
            else
                matched++;
        }
        var added = run.Select(static e => e.Key).Where(key => !baselineKeys.Contains(key)).ToList();
        return new SweepComparison(changed, missing, added, matched);
    }

    internal static string Format(IEnumerable<(string Key, string Body)> entries) =>
        string.Concat(entries.Select(static e => $"## {e.Key}\n{e.Body}"));

    internal static List<(string Key, string Body)> Parse(string text) {
        var entries = new List<(string Key, string Body)>();
        string? key = null;
        var body = new StringBuilder();
        foreach (var line in text.Split('\n')) {
            if (line.StartsWith("## ", StringComparison.Ordinal)) {
                if (key is not null)
                    entries.Add((key, body.ToString()));
                key = line[3..];
                body.Clear();
            }
            else if (key is not null && line.Length > 0)
                body.Append(line).Append('\n');
        }
        if (key is not null)
            entries.Add((key, body.ToString()));
        return entries;
    }

    [Test]
    public async Task EveryBaselineEntry_BindsTheSameOverloads_AndNewEntriesAreListed() {
        var run = Run();
        if (Environment.GetEnvironmentVariable("POLY_SWEEP_OUT") is { Length: > 0 } output) {
            await File.WriteAllTextAsync(output, Format(run));
            return;
        }
        run = [.. run.Select(static e => (e.Key, WithoutExplainedAdditions(e.Body)))];
        var baseline = Parse((await File.ReadAllTextAsync(BaselinePath())).Replace("\r\n", "\n"));
        await Assert.That(baseline.Count).IsGreaterThan(0).Because("an empty baseline compares nothing");
        for (var i = 0; i < baseline.Count; i++) {
            foreach (var (key, before, after, why) in ExplainedDeltas.Where(d => d.Key == baseline[i].Key)) {
                var body = baseline[i].Body;
                var first = body.IndexOf(before, StringComparison.Ordinal);
                await Assert.That(first).IsGreaterThanOrEqualTo(0).Because(why);
                await Assert.That(body.IndexOf(before, first + 1, StringComparison.Ordinal)).IsEqualTo(-1);
                baseline[i] = (key, body.Replace(before, after, StringComparison.Ordinal));
            }
        }
        await Assert.That(run.Select(static e => e.Key).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(run.Count)
            .Because("a key names one document or unit");
        await Assert.That(run.Sum(static e => e.Body.Split('\n').Count(static l => l.StartsWith("  ", StringComparison.Ordinal) && !l.StartsWith("  errors:", StringComparison.Ordinal) && !l.StartsWith("  status:", StringComparison.Ordinal))))
            .IsGreaterThan(0).Because("a run that records no identity proves nothing");

        foreach (var (key, why) in RunDependentErrorCounts)
            await Assert.That(baseline.Any(e => e.Key == key && e.Body.Split('\n').All(static l => l.Length == 0 || l.StartsWith("  errors:", StringComparison.Ordinal)))).IsTrue().Because(why);
        var comparison = Compare(
            [.. baseline.Select(static e => (e.Key, WithoutRunDependentErrorCount(e.Key, e.Body)))],
            [.. run.Select(static e => (e.Key, WithoutRunDependentErrorCount(e.Key, e.Body)))]);
        if (comparison.Added.Count > 0)
            Console.WriteLine($"OverloadSweep: {comparison.Added.Count} entries not in the baseline (reported, not compared):\n  " + string.Join("\n  ", comparison.Added));
        await Assert.That(comparison.Missing).IsEmpty();
        await Assert.That(comparison.Changed).IsEmpty();
        await Assert.That(comparison.Matched).IsEqualTo(baseline.Count);
    }

    // Each explained addition is a real identity of the run, absent from the baseline, of a method with one overload.
    [Test]
    public async Task ExplainedAdditions_AreNewSitesOfASingleOverloadMethod() {
        var run = Run();
        var baseline = await File.ReadAllTextAsync(BaselinePath());
        foreach (var (identity, why) in ExplainedAdditions) {
            await Assert.That(run.Any(e => e.Body.Split('\n').Contains("  " + identity))).IsTrue().Because(why);
            await Assert.That(baseline).DoesNotContain(identity).Because(why);
            var open = identity.IndexOf('(', StringComparison.Ordinal);
            var dot = identity.LastIndexOf('.', open);
            var type = typeof(Poly.Runtime.ActionScope).Assembly.GetType(identity[..dot])!;
            var overloads = type.GetMethods().Count(m => m.Name == identity[(dot + 1)..open]);
            await Assert.That(overloads).IsEqualTo(1).Because(why);
        }
        await Assert.That(WithoutExplainedAdditions("  errors: 0\n  " + ExplainedAdditions[0].Identity + "\n  X.M(Int32)\n"))
            .IsEqualTo("  errors: 0\n  X.M(Int32)\n");
    }

    [Test]
    public async Task Comparer_FailsAChangedEntry_FailsAMissingEntry_ListsAnAddedEntry() {
        (string, string)[] baseline = [("a", "  errors: 0\n  X.M(Int32)\n"), ("b", "  errors: 0\n  X.N(Int32)\n"), ("c", "  errors: 0\n  X.O(Int32)\n")];
        (string, string)[] run = [("a", "  errors: 0\n  X.M(Int64)\n"), ("c", "  errors: 0\n  X.O(Int32)\n"), ("d", "  errors: 0\n  X.P(Int32)\n")];
        var result = Compare(baseline, run);
        await Assert.That(result.Changed).IsEquivalentTo(["a"]);
        await Assert.That(result.Missing).IsEquivalentTo(["b"]);
        await Assert.That(result.Added).IsEquivalentTo(["d"]);
        await Assert.That(result.Matched).IsEqualTo(1);
        // Only an added entry: nothing to fail on.
        var onlyAdded = Compare(baseline, [.. baseline, ("e", "  errors: 0\n")]);
        await Assert.That(onlyAdded.Changed).IsEmpty();
        await Assert.That(onlyAdded.Missing).IsEmpty();
        await Assert.That(onlyAdded.Added).IsEquivalentTo(["e"]);
        await Assert.That(onlyAdded.Matched).IsEqualTo(3);
    }

    [Test]
    public async Task ParseAndFormat_RoundTrip() {
        (string, string)[] entries = [("a b", "  errors: 0\n  X.M(Int32)\n"), ("c", "  status: exception InvalidOperationException\n")];
        await Assert.That(Parse(Format(entries))).IsEquivalentTo(entries);
    }

    [Test]
    public async Task Sweep_CoversEnoughSites_ToBeAnInstrument() {
        var run = Run();
        var identities = run.Sum(static e => e.Body.Split('\n').Count(static l => l.StartsWith("  ", StringComparison.Ordinal) && !l.StartsWith("  errors:", StringComparison.Ordinal) && !l.StartsWith("  status:", StringComparison.Ordinal)));
        await Assert.That(run.Count(static e => e.Key.StartsWith("poly ", StringComparison.Ordinal))).IsGreaterThan(40);
        await Assert.That(run.Count(static e => e.Key.StartsWith("csharp ", StringComparison.Ordinal))).IsGreaterThan(20);
        await Assert.That(identities).IsGreaterThan(200);
    }

    private static List<(string Key, string Body)> Run() {
        var entries = new List<(string Key, string Body)>();
        foreach (var (name, source) in PolyDocuments())
            entries.Add(($"poly {name}", SweepPoly(source)));
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (source, unit) in NodeFactCorpus.Units) {
            // The printed goldens name types a sibling golden declares (Order in OrderItem.cs), and whether those resolve
            // depends on what the run has already registered: the same unit threw alone and analyzed in the full suite.
            // The domains they print are swept above, from their .poly sources.
            if (source.StartsWith(GoldenPrefix, StringComparison.Ordinal))
                continue;
            // A unit is its source path and its ordinal within that source, never a position in the whole corpus.
            var separator = source.IndexOf(": ", StringComparison.Ordinal);
            var path = separator > 0 ? source[..separator] : "?";
            var ordinal = ordinals.GetValueOrDefault(path);
            ordinals[path] = ordinal + 1;
            entries.Add(($"csharp {path} #{ordinal}", SweepCSharp(unit)));
        }
        return entries;
    }

    private static IEnumerable<(string Name, string Source)> PolyDocuments() {
        var guideOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
        var root = GoldenDomains.RepoRoot;
        var files = Directory.EnumerateFiles(root, "*.poly", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)
                .Any(segment => SkippedDirectories.Contains(segment, StringComparer.Ordinal)))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);
        foreach (var relative in files)
            yield return ($"file {relative}", File.ReadAllText(Path.Combine(root, relative)));

        foreach (var (id, wrapper) in GuideBlocks.Wrappers().OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            yield return ($"wrapper {id}", GuideBlocks.Splice(wrapper, [""]).Text);

        var guide = GuideBlocks.Load();
        foreach (var block in guide.Blocks) {
            string composed;
            try {
                composed = (block.Marker is null ? GuideBlocks.Probe(block) : GuideBlocks.Compose(block, guide.Wrappers)).Text;
            }
            catch (InvalidOperationException) {
                composed = GuideBlocks.Probe(block).Text;
            }
            var firstLine = block.FirstLine.Length > 60 ? block.FirstLine[..60] : block.FirstLine;
            var ordinal = guideOrdinals.GetValueOrDefault(firstLine);
            guideOrdinals[firstLine] = ordinal + 1;
            yield return ($"guide {firstLine} #{ordinal}", composed);
        }
    }

    private static string SweepPoly(string source) {
        var text = new StringBuilder();
        try {
            var session = DomainSession.ForSource(source, Seed, Catalog);
            var changes = DomainCompilation.WithSeed(new PolyDslParser(source, session).Parse(), Seed);
            var outcome = new DomainEvolution(new Domain("_", [])).ApplyAndAnalyze(changes, session: session);
            if (!outcome.Succeeded) {
                var codes = outcome.Analysis.Diagnostics
                    .Where(static d => d.Severity == DiagnosticSeverity.Error)
                    .Select(static d => d.Code)
                    .Order(StringComparer.Ordinal);
                text.Append("  status: analysis-errors ").Append(string.Join(',', codes)).Append('\n');
                return text.ToString();
            }
            var compiled = session.Compile(outcome.Root, outcome.Analysis);
            AppendIdentities(text, compiled.VmAnalysis);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) {
            text.Append("  status: exception ").Append(ex.GetType().Name).Append('\n');
        }
        return text.ToString();
    }

    private static string SweepCSharp(Node unit) {
        var text = new StringBuilder();
        try {
            AppendIdentities(text, Interpreter.Analyze(unit));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) {
            text.Append("  status: exception ").Append(ex.GetType().Name).Append('\n');
        }
        return text.ToString();
    }

    private static void AppendIdentities(StringBuilder text, AnalysisResult analysis) {
        var errors = analysis.Diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Error);
        text.Append("  errors: ").Append(errors).Append('\n');
        foreach (var site in analysis.GetCallSiteCatalog() ?? [])
            text.Append("  ").Append(site.Identity).Append('\n');
    }
}