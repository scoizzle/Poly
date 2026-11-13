using System.Text;

using Poly.Compile;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Language;
using Poly.Grammar;
using Poly.Packs.Product;
using Poly.Tests.Docs;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// G-26b parity, part 3: the pack doors. <c>uses http</c>, <c>uses storage</c> and <c>uses temporal</c> (known ids), an unknown id, each
/// plain, pinned three ways (<c>v1</c>, <c>2024-11-20</c>, <c>1.2.3</c>), pinned with a string, and listed twice, read by four doors that
/// each have their own code: the syntax tree's header reader (<c>DslSyntaxTree</c>), the outline (which calls the same reader),
/// the resolver (<c>ErrorAtUse</c> positions its refusal on the id token) and <c>PolyCompiler</c>'s <c>uses</c> scan (its own token loop).
/// The four results per cell are frozen (<c>PackDoorParity.txt</c>); the cross-door claims below hold today and fail when a door drifts.
/// </summary>
public sealed class PackDoorParityTests {
    private const string FilePath = "Poly.Tests/DomainModeling/Parsing/PackDoorParity.txt";

    internal static readonly string[] Ids = ["http", "storage", "temporal", "zz"];

    internal static readonly (string Form, Func<string, string> Lines)[] Forms = [
        ("plain", id => $"uses {id}"),
        ("pin-ident", id => $"uses {id} v1"),
        ("pin-date", id => $"uses {id} 2024-11-20"),
        ("pin-dotted", id => $"uses {id} 1.2.3"),
        ("pin-string", id => $"uses {id} \"v1\""),
        ("duplicate", id => $"uses {id}\nuses {id}"),
    ];

    private static string Document(string usesLines) => $"domain A\n{usesLines}\nE: entity {{\n  N: Text\n}}\n";

    // ── the four doors ───────────────────────────────────────────────────────────────

    internal static string Tree(string text) {
        try {
            var tree = DslSyntaxTree.Parse(text);
            var pins = string.Join("|", tree.UsesVersions.Select(v => v is null ? "<none>" : $"[{ResolverErrorBaseline.Escape(v)}]"));
            return $"uses=[{string.Join("|", tree.Uses)}] pins={pins} headerEnd={tree.HeaderEnd}";
        }
        catch (Exception ex) {
            return $"ERR {ex.GetType().Name}: {ResolverErrorBaseline.Escape(ex.Message)}";
        }
    }

    internal static string Outline(string text) {
        try {
            var nodes = DslOutline.Build(text).All().Where(n => n.Kind == "uses")
                .Select(n => $"{n.Path}@{n.StartLine}:{n.StartOffset}-{n.EndOffset}");
            return "nodes=[" + string.Join("|", nodes) + "]";
        }
        catch (Exception ex) {
            return $"ERR {ex.GetType().Name}: {ResolverErrorBaseline.Escape(ex.Message)}";
        }
    }

    internal static string Resolver(string text) {
        var exception = ResolverErrorSiteScanner.Capture(text);
        return exception is null ? "OK" : ResolverErrorBaseline.ResultOf(exception);
    }

    private static PolyCompiler? _compiler;

    internal static string Compiler(string text) {
        try {
            _compiler ??= new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.ProductAuthoring));
            var errors = _compiler.Analyze(text, "pack.poly").Diagnostics.Where(d => d.IsError)
                .Select(d => $"{d.Code}@{d.Line}:{d.Column} {ResolverErrorBaseline.Escape(d.Message)}");
            return "errors=[" + string.Join("|", errors) + "]";
        }
        catch (Exception ex) {
            return $"ERR {ex.GetType().Name}: {ResolverErrorBaseline.Escape(ex.Message)}";
        }
    }

    internal static IEnumerable<(string Key, string Text)> Cells() {
        foreach (var id in Ids) {
            foreach (var (form, lines) in Forms)
                yield return ($"{id}|{form}", Document(lines(id)));
        }
    }

    internal static string Row(string key, string text) =>
        $"P\t{key}\t{ResolverErrorBaseline.Escape(Tree(text))}\t{ResolverErrorBaseline.Escape(Outline(text))}\t{ResolverErrorBaseline.Escape(Resolver(text))}\t{ResolverErrorBaseline.Escape(Compiler(text))}";

    internal static List<string> ReadRows() =>
        [.. File.ReadAllLines(Path.Combine(GuideBlocks.RepoRoot(), FilePath)).Where(l => l.StartsWith("P\t", StringComparison.Ordinal))];

    /// <summary>The frozen rows against freshly computed ones, cell by cell. Pure so the self-test mutates a copy.</summary>
    internal static List<string> Compare(IReadOnlyList<string> frozen, IReadOnlyList<string> actual) {
        var failures = new List<string>();
        var now = actual.ToDictionary(r => r.Split('\t')[1], StringComparer.Ordinal);
        foreach (var row in frozen) {
            var key = row.Split('\t')[1];
            if (!now.TryGetValue(key, out var seen))
                failures.Add($"{key}: not a cell any more");
            else if (seen != row)
                failures.Add($"{key}\n    expected {row}\n    actual   {seen}");
        }
        foreach (var key in now.Keys.Except(frozen.Select(r => r.Split('\t')[1])))
            failures.Add($"{key}: a cell with no frozen row");
        return failures;
    }

    [Test]
    public async Task Regenerate_WritesThePackDoorRows() {
        if (Environment.GetEnvironmentVariable("POLY_REGEN_PACK_DOORS") is null)
            return;
        var lines = new List<string> {
            "# G-26b: what each door does with a uses line. P id|form<TAB>tree<TAB>outline<TAB>resolver<TAB>compiler. See PackDoorParityTests.",
        };
        lines.AddRange(Cells().Select(c => Row(c.Key, c.Text)));
        File.WriteAllLines(Path.Combine(GuideBlocks.RepoRoot(), FilePath), lines);
        await Assert.That(lines.Count).IsGreaterThan(20);
    }

    [Test]
    public async Task Doors_MatchTheFrozenRows() =>
        await Assert.That(Compare(ReadRows(), [.. Cells().Select(c => Row(c.Key, c.Text))])).IsEmpty();

    // ── the cross-door claims ────────────────────────────────────────────────────────

    private static List<DslToken> Tokens(string text) {
        var tokens = new List<DslToken>();
        var reader = new DslTokenReader(text);
        while (reader.Peek().Kind != DslTokenKind.EndOfFile) {
            tokens.Add(reader.Peek());
            reader.Consume(1);
        }

        return tokens;
    }

    [Test]
    public async Task TreeAndOutline_NameTheSameIdsInTheSameOrder_AndTheTreeRefusesADuplicate() {
        var failures = new List<string>();
        foreach (var (key, text) in Cells()) {
            var outline = DslOutline.Build(text).All().Where(n => n.Kind == "uses").Select(n => n.Name).ToList();
            if (key.EndsWith("|duplicate", StringComparison.Ordinal)) {
                var id = key.Split('|')[0];
                if (!outline.SequenceEqual([id, id]))
                    failures.Add($"{key}: the outline lists {string.Join(",", outline)}");
                try {
                    DslSyntaxTree.Parse(text);
                    failures.Add($"{key}: the header reader accepted a duplicate id");
                }
                catch (FormatException) {
                }
                continue;
            }
            var tree = DslSyntaxTree.Parse(text);
            if (!tree.Uses.SequenceEqual(outline))
                failures.Add($"{key}: tree {string.Join(",", tree.Uses)} outline {string.Join(",", outline)}");
        }

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task ResolverAndCompilerRefusals_AreOnTheLineAndColumnOfTheIdTheTokensHave() {
        var failures = new List<string>();
        foreach (var (key, text) in Cells()) {
            var tokens = Tokens(text);
            var id = key.Split('|')[0];
            var idTokens = Enumerable.Range(1, tokens.Count - 1).Where(i => tokens[i - 1].Kind == DslTokenKind.Uses).Select(i => tokens[i]).ToList();

            if (ResolverErrorSiteScanner.Capture(text) is GrammarException g && g.Detail.Contains($"'{id}'", StringComparison.Ordinal)
                && (g.Line, g.Column) != (idTokens[0].Line, idTokens[0].Col))
                failures.Add($"{key}: resolver positions its refusal at {g.Line}:{g.Column}, the id token is at {idTokens[0].Line}:{idTokens[0].Col}");

            _compiler ??= new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.ProductAuthoring));
            foreach (var d in _compiler.Analyze(text, "pack.poly").Diagnostics.Where(d => d.IsError && d.Message.Contains($"'{id}'", StringComparison.Ordinal))) {
                // A duplicate is placed on the second occurrence; every other id finding on the first.
                var expected = d.Code == DiagnosticCodes.DuplicateExtension ? idTokens[1] : idTokens[0];
                if ((d.Line, d.Column) != (expected.Line, expected.Col))
                    failures.Add($"{key}: compiler {d.Code} at {d.Line}:{d.Column}, expected {expected.Line}:{expected.Col}");
            }
        }

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task EveryKnownIdIsKnownToTheCompiler_AndTheUnknownOneIsRefusedByIt() {
        var rows = ReadRows().Select(r => r.Split('\t')).ToDictionary(f => f[1], f => ResolverErrorBaseline.Unescape(f[5]), StringComparer.Ordinal);

        foreach (var id in new[] { "http", "storage", "temporal" })
            await Assert.That(rows[$"{id}|plain"]).IsEqualTo("errors=[]");
        await Assert.That(rows["zz|plain"]).Contains(DiagnosticCodes.UnknownExtension);
        foreach (var id in Ids)
            await Assert.That(rows[$"{id}|duplicate"]).Contains(DiagnosticCodes.DuplicateExtension);
    }

    /// <summary>
    /// The resolver column of a pack-door cell that is known wrong. An unknown id surfaces as a raw <c>InvalidOperationException</c> with no
    /// position (G-29 positions it); a duplicate id is an unpositioned <c>FormatException</c> thrown by the header reader, which no row of
    /// the programme owns (the compiler re-derives its position by rescanning the tokens). Both are stale-checked.
    /// </summary>
    internal static readonly (string Key, string Fixer, string Fragment)[] KnownWrong = [
        .. Forms.Where(f => f.Form != "duplicate").Select(f => ($"P:zz|{f.Form}", "G-29", "InvalidOperationException @0:0 off=-1 len=0 :: Unknown domain extension 'zz'")),
        .. Ids.Select(id => ($"P:{id}|duplicate", ResolverErrorBaseline.Unowned, $"FormatException @0:0 off=-1 len=0 :: Domain lists extension '{id}' more than once.")),
    ];

    [Test]
    public async Task WrongResolverResults_AreTaggedAndStillWrong() {
        var results = ReadRows().Select(r => r.Split('\t')).ToDictionary(f => "P:" + f[1], f => ResolverErrorBaseline.Unescape(f[4]), StringComparer.Ordinal);

        await Assert.That(ResolverErrorBaseline.TagProblems(results, KnownWrong)).IsEmpty();
    }

    // ── self-tests ───────────────────────────────────────────────────────────────────

    [Test]
    public async Task SelfTest_OneDoorResultChanged_FailsTheFrozenRow() {
        var frozen = ReadRows();
        var mutated = frozen.ToList();
        mutated[0] = mutated[0].Replace("uses=[", "uses=[x", StringComparison.Ordinal);
        var failures = Compare(mutated, [.. Cells().Select(c => Row(c.Key, c.Text))]);

        await Assert.That(failures).IsNotEmpty();
    }

    [Test]
    public async Task SelfTest_ATagWhoseFragmentNoLongerAppears_IsStale() {
        var results = ReadRows().Select(r => r.Split('\t')).ToDictionary(f => "P:" + f[1], f => ResolverErrorBaseline.Unescape(f[4]), StringComparer.Ordinal);
        var tags = KnownWrong.Select(t => t.Key == "P:zz|plain" ? (t.Key, t.Fixer, "Unknown domain extension 'qq'") : t).ToArray();

        await Assert.That(ResolverErrorBaseline.TagProblems(results, tags).Any(p => p.Contains("stale tag", StringComparison.Ordinal))).IsTrue();
    }
}