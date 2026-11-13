using System.Security.Cryptography;
using System.Text;

using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Product;
using Poly.Tests.Docs;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// The byte-identical print baseline (G-26a, amendment 9 of the G-26 lock): one keyed file,
/// <c>PrintBaseline.txt</c>, holds the exact text <see cref="DomainDslPrinter"/> prints for every
/// document of the corpus. The idempotence guard (<c>DslRoundTripCorpusTests</c>) cannot see a printer
/// change that is stable after one round; this file can: every byte of every printed document is pinned.
///
/// <para><b>Keys.</b> <c>poly/&lt;repo-relative path&gt;</c> for every shipped <c>*.poly</c>;
/// <c>pack/&lt;name&gt;</c> for each <see cref="PackOracleDomains.All"/> fixture;
/// <c>guide/&lt;first line&gt;</c> for each guide fence that is a whole document or a fragment with a
/// wrapper (spliced as <c>GuideBlocks.Compose</c> does); <c>built/&lt;name&gt;</c> for domains built in
/// code, one per printer branch no document reaches (see <see cref="BuiltCases"/> for the list and the
/// derivation). The golden domains (<see cref="GoldenDomains"/>) are files among the shipped documents,
/// and a test proves that.</para>
///
/// <para><b>Per key, the session and the pipeline.</b> The header of each section names both. A
/// <c>document</c> key is printed through the same pipeline as <c>DslRoundTripCorpusTests.Generate</c>:
/// the session is <c>DomainSession.Create(uses of the document, ExtensionCatalog.Core + ProductCatalog.ImplicitIds)</c>;
/// the document is parsed, applied and analysed (<c>ApplyAndAnalyze</c>), and the committed root is printed
/// through <c>new DomainDslPrinter(session)</c>. A key whose document does not analyse is recorded as
/// <c>analysis-failed</c> (or <c>parse-failed</c>) with the exception or diagnostic type: it is not skipped
/// (<c>docs/missions/evolve-2026-10-13/library.poly</c> is one, #857). A <c>built</c> key is printed
/// straight from a code-built <see cref="Domain"/> through a printer whose session the header names; there is
/// no parse and no analysis.</para>
///
/// <para><b>Trivia.</b> The printer drops comments: 12 of the 42 shipped files carry <c>//</c> lines and
/// none of them is in the baseline. That is frozen as designed. Line endings are normalised to <c>\n</c>.</para>
///
/// <para><b>Regeneration.</b> <c>POLY_REGEN_PRINT_BASELINE=1</c> rewrites the file from the CURRENT printer
/// (run <c>Regenerate_WritesBaselineFromCurrentPrinter</c>). Regenerate only on an UNCHANGED printer, for example
/// when a document is added. A later row that moves a byte names the key and why in its PR, and re-records
/// that key only: never regenerate to make a red test green without reading the diff.</para>
///
/// <para><b>Self-tests.</b> <see cref="Compare"/> is a pure function over two entry lists;
/// <c>Compare_*</c> tests mutate a COPY of the frozen entries (a dropped space, a changed key order) and
/// assert the oracle fails, so the oracle's ability to fail is committed, not described.</para>
/// </summary>
public sealed class PrintBaselineTests {
    private const string BaselinePath = "Poly.Tests/DomainModeling/Parsing/PrintBaseline.txt";

    private const string HeaderPrefix = "##### ";

    /// <summary>The number of shipped <c>*.poly</c> files (git ls-files, 2026-11-13). Pinned: a new document is a visible edit.</summary>
    internal const int ShippedPolyFiles = 42;

    /// <summary>
    /// The documents that do not print today, by key, with the exact recorded status (the failure under each catalog tried).
    /// None has an owner row: they are mission and probe documents that fail analysis, one by design
    /// (<c>nested-invoke-type-mismatch</c>), one known (<c>library.poly</c>, #857), and <c>pack/Warehouse620</c>,
    /// which does not parse. Stale-checked both ways: a document that starts to print fails until its entry is deleted
    /// and its key re-recorded; a status that changes fails until re-recorded.
    /// </summary>
    internal static readonly (string Key, string Status)[] KnownUnprintable = [
        ("poly/docs/missions/evolve-2026-10-13/ambig-backref.poly", "Core:analysis-failed:DMREL005 | Product:analysis-failed:DMREL005"),
        ("poly/docs/missions/evolve-2026-10-13/billing.poly", "Core:analysis-failed:DMEFF011+DMLIB001 | Product:analysis-failed:DMEFF011+DMLIB001"),
        ("poly/docs/missions/evolve-2026-10-13/claims.poly", "Core:analysis-failed:DMEFF011+DMLIB001 | Product:analysis-failed:DMEFF011+DMLIB001"),
        ("poly/docs/missions/evolve-2026-10-13/disputes.poly", "Core:analysis-failed:DMSTR006 | Product:analysis-failed:DMSTR006"),
        ("poly/docs/missions/evolve-2026-10-13/library.poly", "Core:session-failed:InvalidOperationException | Product:analysis-failed:DMEFF011+DMLIB001"),
        ("poly/docs/missions/evolve-2026-10-13/university-http.poly", "Core:session-failed:InvalidOperationException | Product:analysis-failed:DMEFF011+DMLIB001"),
        ("poly/docs/missions/evolve-2026-10-13/university.poly", "Core:analysis-failed:DMEFF011+DMLIB001 | Product:analysis-failed:DMEFF011+DMLIB001"),
        ("poly/docs/missions/evolve-2026-10-14/ratelimit.poly", "Core:analysis-failed:DMLIB001 | Product:analysis-failed:DMLIB001"),
        ("poly/docs/missions/evolve-2026-10-14/sessions.poly", "Core:analysis-failed:DMLIB001 | Product:analysis-failed:DMLIB001"),
        ("poly/docs/probes/dogfood/nested-invoke-type-mismatch.poly", "Core:analysis-failed:DMSEM003 | Product:analysis-failed:DMSEM003"),
        ("poly/docs/probes/scratch-2026-10-14/restaurant.poly", "Core:analysis-failed:DMEFF011+DMSTR006 | Product:analysis-failed:DMEFF011+DMSTR006"),
        ("pack/Warehouse620", "Core:session-failed:InvalidOperationException | Product:failed:GrammarException"),
    ];

    // ── entries ──────────────────────────────────────────────────────────────────────

    /// <summary>One section of the baseline file.</summary>
    internal sealed record Entry(string Key, string Pipeline, string Session, string Status, string Text) {
        public string Sha8 => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Text)))[..8];
    }

    private const string PipelineDocument = "document: parse > ApplyAndAnalyze > print(DomainSession.Create(uses, Core+ImplicitIds))";

    private const string PipelineBuilt = "built: print(code-built Domain)";

    private static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    // ── the pure oracle ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Compares the frozen entries with the current ones. Returns null when they are byte-identical, otherwise a
    /// readable report: the key set and order first, then the first differing key with its first differing line.
    /// </summary>
    internal static string? Compare(IReadOnlyList<Entry> frozen, IReadOnlyList<Entry> current) {
        var frozenKeys = frozen.Select(e => e.Key).ToList();
        var currentKeys = current.Select(e => e.Key).ToList();
        if (!frozenKeys.SequenceEqual(currentKeys, StringComparer.Ordinal)) {
            var missing = frozenKeys.Except(currentKeys, StringComparer.Ordinal).ToList();
            var added = currentKeys.Except(frozenKeys, StringComparer.Ordinal).ToList();
            var detail = missing.Count == 0 && added.Count == 0
                ? "the same keys in a different order; first difference at index "
                  + frozenKeys.Zip(currentKeys, (a, b) => a == b).TakeWhile(same => same).Count()
                : $"missing from the printer run: [{string.Join(", ", missing)}]; not in the baseline: [{string.Join(", ", added)}]";
            return "The key list differs from the baseline: " + detail;
        }

        for (var i = 0; i < frozen.Count; i++) {
            var f = frozen[i];
            var c = current[i];
            if (f.Pipeline != c.Pipeline || f.Session != c.Session || f.Status != c.Status)
                return $"key {f.Key}: the recorded pipeline, session or status differs.\n"
                       + $"  baseline: {f.Pipeline} | {f.Session} | {f.Status}\n"
                       + $"  current:  {c.Pipeline} | {c.Session} | {c.Status}";
            if (string.Equals(f.Text, c.Text, StringComparison.Ordinal))
                continue;
            var expectedLines = f.Text.Split('\n');
            var actualLines = c.Text.Split('\n');
            var line = 0;
            while (line < expectedLines.Length && line < actualLines.Length && expectedLines[line] == actualLines[line])
                line++;
            var expectedLine = line < expectedLines.Length ? expectedLines[line] : "<end of text>";
            var actualLine = line < actualLines.Length ? actualLines[line] : "<end of text>";
            return $"key {f.Key}: the printed text differs at line {line + 1} "
                   + $"(baseline {f.Sha8}, current {c.Sha8}).\n  expected: {Visible(expectedLine)}\n  actual:   {Visible(actualLine)}";
        }

        return null;
    }

    private static string Visible(string line) => DslTokenStreamBaselineTests.Escape(line) + "$";

    // ── the file ─────────────────────────────────────────────────────────────────────

    internal static string Render(IReadOnlyList<Entry> entries) {
        var sb = new StringBuilder();
        sb.Append("# PrintBaseline.txt: the exact text DomainDslPrinter prints, one section per key.\n");
        sb.Append("# Header: ##### key <TAB> pipeline <TAB> session <TAB> status <TAB> sha8 of the text. See PrintBaselineTests.\n");
        sb.Append("# Regenerate only on an unchanged printer (POLY_REGEN_PRINT_BASELINE=1); comments in the sources are dropped by design.\n");
        foreach (var entry in entries) {
            sb.Append(HeaderPrefix).Append(entry.Key).Append('\t').Append(entry.Pipeline).Append('\t').Append(entry.Session)
                .Append('\t').Append(entry.Status).Append('\t').Append(entry.Sha8).Append('\n');
            sb.Append(entry.Text);
            if (entry.Text.Length > 0 && !entry.Text.EndsWith('\n'))
                sb.Append('\n');
        }

        return sb.ToString();
    }

    internal static List<Entry> Parse(string file) {
        var entries = new List<Entry>();
        string[]? header = null;
        var text = new StringBuilder();
        void Flush() {
            if (header is null)
                return;
            var entry = new Entry(header[0], header[1], header[2], header[3], text.ToString());
            if (!string.Equals(entry.Sha8, header[4], StringComparison.Ordinal))
                throw new InvalidOperationException($"PrintBaseline key {header[0]}: the recorded sha8 {header[4]} does not match the text ({entry.Sha8}); the file was edited by hand.");
            entries.Add(entry);
            text.Clear();
        }

        var lines = Normalise(file).Split('\n');
        var count = lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        for (var i = 0; i < count; i++) {
            var line = lines[i];
            if (line.StartsWith(HeaderPrefix, StringComparison.Ordinal)) {
                Flush();
                var fields = line[HeaderPrefix.Length..].Split('\t');
                if (fields.Length != 5)
                    throw new InvalidOperationException($"PrintBaseline header has {fields.Length} fields, expected 5: {line}");
                header = fields;
                continue;
            }

            if (header is null) {
                if (line.Length == 0 || line[0] == '#')
                    continue;
                throw new InvalidOperationException($"PrintBaseline text before the first header: {line}");
            }

            text.Append(line).Append('\n');
        }

        Flush();
        return entries;
    }

    // ── producing the entries ────────────────────────────────────────────────────────

    /// <summary>The shipped <c>*.poly</c> files, repo-relative with forward slashes, ordinal order.</summary>
    internal static IReadOnlyList<string> ShippedDocuments() {
        var root = GuideBlocks.RepoRoot();
        var skip = new[] { "/bin/", "/obj/", "/.git/", "/node_modules/" };
        return [.. Directory.EnumerateFiles(root, "*.poly", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
            .Where(p => !skip.Any(s => ("/" + p).Contains(s, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>The guide's whole-document and wrapper-spliced fences, keyed by first line (ordinal suffix on a repeat).</summary>
    internal static IReadOnlyList<(string Key, string Text)> GuideDocuments() {
        var document = GuideBlocks.Load();
        var wrappers = GuideBlocks.Wrappers();
        var result = new List<(string, string)>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var fence in document.Fences.Where(f => string.Equals(f.Info, "poly", StringComparison.Ordinal))) {
            if (fence.Marker is not { Kind: GuideMarkerKind.Valid or GuideMarkerKind.Fragment } marker)
                continue;
            var block = new GuideBlock(fence.Line, fence.Code, fence.Marker);
            var text = GuideBlocks.Compose(block, wrappers).Text;
            var first = block.FirstLine;
            var key = "guide/" + marker.Kind.ToString().ToLowerInvariant() + "/" + first;
            seen[key] = seen.GetValueOrDefault(key) + 1;
            result.Add((seen[key] == 1 ? key : key + " #" + seen[key], text));
        }

        return result;
    }

    /// <summary>
    /// Prints one document through <see cref="PipelineDocument"/>. The catalog is <see cref="ExtensionCatalog.Core"/>, as in
    /// <c>DslRoundTripCorpusTests.Generate</c>. A document that does not print under it (an extension the core catalog does
    /// not know, such as <c>http</c> or <c>sqlite</c>, or a bare <c>unique</c> that needs the product library) is printed through
    /// <see cref="ProductCatalog.Model"/> instead; the header records which catalog printed it. A document that prints under
    /// neither is recorded with the failure of each, so a failure is never silent.
    /// </summary>
    internal static Entry PrintDocument(string key, string poly) {
        var tree = DslSyntaxTree.Parse(poly);
        var sessionText = "uses=[" + string.Join(",", tree.Uses) + "]+implicit";
        var failures = new List<string>();
        foreach (var (name, catalog) in new[] { ("Core", ExtensionCatalog.Core), ("Product", ProductCatalog.Model) }) {
            var pipeline = PipelineDocument + "; catalog=" + name;
            DomainSession session;
            try {
                session = DomainSession.Create(tree.Uses, new SessionOptions(catalog, ProductCatalog.ImplicitIds));
            }
            catch (InvalidOperationException ex) {
                failures.Add(name + ":session-failed:" + ex.GetType().Name);
                continue;
            }

            try {
                var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
                    .ApplyAndAnalyze(new PolyDslParser(poly, session).Parse(), session: session);
                if (result.Succeeded)
                    return new Entry(key, pipeline, sessionText, "printed", Normalise(new DomainDslPrinter(session).Print(result.Root)));
                var codes = string.Join("+", result.Analysis.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Code).Distinct().Order(StringComparer.Ordinal));
                failures.Add(name + ":analysis-failed:" + (codes.Length == 0 ? "mutation" : codes));
            }
            catch (Exception ex) {
                failures.Add(name + ":failed:" + ex.GetType().Name);
            }
        }

        return new Entry(key, PipelineDocument + "; catalog=none", sessionText, string.Join(" | ", failures), "");
    }

    internal static Entry PrintBuilt(string name, DomainSession session, string sessionText, Domain domain, IReadOnlyList<string>? implicitIds = null) {
        var printer = implicitIds is null ? new DomainDslPrinter(session) : new DomainDslPrinter(session, implicitIds);
        return new Entry("built/" + name, PipelineBuilt, sessionText + (implicitIds is null ? "" : "; implicitIds=[" + string.Join(",", implicitIds) + "]"),
            "printed", Normalise(printer.Print(domain)));
    }

    /// <summary>Every entry, in key order: poly/, pack/, guide/, built/.</summary>
    internal static List<Entry> CurrentEntries() {
        var entries = new List<Entry>();
        var root = GuideBlocks.RepoRoot();
        foreach (var path in ShippedDocuments())
            entries.Add(PrintDocument("poly/" + path, File.ReadAllText(Path.Combine(root, path))));
        foreach (var domain in PackOracleDomains.All)
            entries.Add(PrintDocument("pack/" + domain.Name, domain.Poly));
        foreach (var (key, text) in GuideDocuments())
            entries.Add(PrintDocument(key, text));
        entries.AddRange(PrintBaselineBuiltCases.All());
        return entries;
    }

    private static List<Entry> Frozen() =>
        Parse(File.ReadAllText(Path.Combine(GuideBlocks.RepoRoot(), BaselinePath)));

    // ── tests ────────────────────────────────────────────────────────────────────────

    /// <summary>Writes the baseline from the CURRENT printer. Inert unless <c>POLY_REGEN_PRINT_BASELINE</c> is set.</summary>
    [Test]
    public async Task Regenerate_WritesBaselineFromCurrentPrinter() {
        if (Environment.GetEnvironmentVariable("POLY_REGEN_PRINT_BASELINE") is null)
            return;

        var entries = CurrentEntries();
        File.WriteAllText(Path.Combine(GuideBlocks.RepoRoot(), BaselinePath), Render(entries));
        await Assert.That(entries.Count).IsGreaterThan(40);
    }

    [Test]
    public async Task Print_IsByteIdenticalToTheFrozenBaseline() {
        var report = Compare(Frozen(), CurrentEntries());

        await Assert.That(report).IsNull().Because(report ?? "");
    }

    [Test]
    public async Task Documents_AreAllShippedPolyFiles() {
        await Assert.That(ShippedDocuments().Count).IsEqualTo(ShippedPolyFiles);
        var keys = Frozen().Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var path in ShippedDocuments())
            await Assert.That(keys.Contains("poly/" + path)).IsTrue().Because(path);
    }

    [Test]
    public async Task GoldenDomains_AreAmongTheShippedDocuments() {
        var keys = Frozen().Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var golden in GoldenDomains.All.Where(g => !g.Path.StartsWith("Poly.Tests/Corpus/swarm/", StringComparison.Ordinal)))
            await Assert.That(keys.Contains("poly/" + golden.Path)).IsTrue().Because(golden.Path);
    }

    [Test]
    public async Task PackAndGuideKeys_AreCoveredAndCounted() {
        var frozen = Frozen();
        await Assert.That(frozen.Count(e => e.Key.StartsWith("pack/", StringComparison.Ordinal))).IsEqualTo(PackOracleDomains.All.Count);
        await Assert.That(frozen.Count(e => e.Key.StartsWith("guide/", StringComparison.Ordinal))).IsEqualTo(GuideDocuments().Count);
        await Assert.That(frozen.Count(e => e.Key.StartsWith("built/", StringComparison.Ordinal))).IsEqualTo(PrintBaselineBuiltCases.All().Count);
    }

    /// <summary>A document that does not print is recorded as such: exactly the known set, with the exact status, in both directions.</summary>
    [Test]
    public async Task UnprintableDocuments_AreRecorded_ExactlyAsKnown() {
        var recorded = Frozen().Where(e => e.Status != "printed").ToDictionary(e => e.Key, e => e.Status, StringComparer.Ordinal);
        var known = KnownUnprintable.ToDictionary(k => k.Key, k => k.Status, StringComparer.Ordinal);

        foreach (var (key, status) in recorded)
            await Assert.That(known.TryGetValue(key, out var knownStatus) && status == knownStatus).IsTrue()
                .Because($"{key} is recorded as '{status}' but is not a known unprintable document with that status");
        foreach (var key in known.Keys)
            await Assert.That(recorded.ContainsKey(key)).IsTrue()
                .Because($"{key} is listed as unprintable but is recorded as printed: delete the entry (stale)");
    }

    [Test]
    public async Task PrintedText_NeverStartsALineWithTheHeaderPrefix() {
        foreach (var entry in Frozen())
            await Assert.That(entry.Text.Split('\n').Any(l => l.StartsWith(HeaderPrefix, StringComparison.Ordinal))).IsFalse().Because(entry.Key);
    }

    /// <summary>The file round-trips through <see cref="Render"/> and <see cref="Parse"/> exactly.</summary>
    [Test]
    public async Task File_IsCanonical() {
        var file = Normalise(File.ReadAllText(Path.Combine(GuideBlocks.RepoRoot(), BaselinePath)));

        await Assert.That(Render(Frozen())).IsEqualTo(file);
    }

    // ── self-tests: the oracle fails on a mutated copy ───────────────────────────────

    [Test]
    public async Task Compare_Passes_WhenNothingIsMutated() {
        var frozen = Frozen();

        await Assert.That(Compare(frozen, [.. frozen])).IsNull();
    }

    [Test]
    public async Task Compare_Fails_WhenOneSpaceIsDroppedInOnePrintedForm() {
        var frozen = Frozen();
        var index = frozen.FindIndex(e => e.Text.Contains("key(", StringComparison.Ordinal) || e.Text.Contains(": entity {", StringComparison.Ordinal));
        var victim = frozen[index];
        var mutated = victim with { Text = victim.Text.Replace(": entity {", ": entity{", StringComparison.Ordinal) };
        var copy = new List<Entry>(frozen) { [index] = mutated };

        var report = Compare(frozen, copy);

        await Assert.That(report).IsNotNull();
        await Assert.That(report!).Contains(victim.Key);
        await Assert.That(report).Contains("differs at line");
    }

    [Test]
    public async Task Compare_Fails_WhenTheKeyOrderChanges() {
        var frozen = Frozen();
        var copy = new List<Entry>(frozen) { [0] = frozen[1], [1] = frozen[0] };

        var report = Compare(frozen, copy);

        await Assert.That(report).IsNotNull();
        await Assert.That(report!).Contains("different order");
    }

    [Test]
    public async Task Compare_Fails_WhenAKeyIsMissing() {
        var frozen = Frozen();

        var report = Compare(frozen, [.. frozen.Skip(1)]);

        await Assert.That(report).IsNotNull();
        await Assert.That(report!).Contains(frozen[0].Key);
    }

    [Test]
    public async Task Compare_Fails_WhenTheRecordedSessionChanges() {
        var frozen = Frozen();
        var copy = new List<Entry>(frozen) { [0] = frozen[0] with { Session = frozen[0].Session + "+x" } };

        await Assert.That(Compare(frozen, copy)).IsNotNull();
    }
}
