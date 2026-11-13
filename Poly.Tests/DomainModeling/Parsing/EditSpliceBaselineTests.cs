using System.Text;
using System.Text.Json;

using Poly.DomainModeling.Language;
using Poly.Tests.Docs;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// G-26b parity, part 2: <c>PolyEdit</c>'s splice results, frozen. For the first addressable node of each kind in each valid document
/// (the 42 shipped <c>.poly</c> and G-13a's sweep documents) three ops run: <c>replace</c> with the node's own text, <c>remove</c>, and
/// <c>add</c> of the node's own text under its parent. Each row records the result: the edited text's SHA and its length change, whether
/// the replace was byte-identical, or the error code.
///
/// <para>The replace rows carry <c>identical=Y</c> or <c>N</c>; the invariant test asserts none is <c>N</c>.</para>
/// </summary>
public sealed class EditSpliceBaselineTests {
    private const string FilePath = "Poly.Tests/DomainModeling/Parsing/EditSpliceBaseline.txt";

    internal static string Result(string source, EditOutcome outcome, bool replace) {
        if (!outcome.Ok)
            return "err " + outcome.ErrorCode;
        var identical = replace ? (outcome.Text == source ? "Y" : "N") : "-";
        return $"ok identical={identical} delta={outcome.Text!.Length - source.Length} sha={ResolverErrorBaseline.Sha(outcome.Text)}";
    }

    private static string Json(string op, string? parent, string? path, string? dsl) => op switch {
        "add" => JsonSerializer.Serialize(new[] { new { op, parent, dsl } }),
        "remove" => JsonSerializer.Serialize(new[] { new { op, path } }),
        _ => JsonSerializer.Serialize(new[] { new { op, path, dsl } }),
    };

    internal static List<(string Key, string Result)> ComputeRows() {
        var rows = new List<(string, string)>();
        foreach (var (doc, text) in ParityDocuments.All) {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in DslOutline.Build(text).All()) {
                if (!node.Addressable || node.Path.Length == 0 || !seen.Add(node.Kind))
                    continue;
                var own = text[node.StartOffset..node.EndOffset];
                var dot = node.Path.LastIndexOf('.');
                var parent = node.Path.StartsWith("uses:", StringComparison.Ordinal) || dot < 0 ? "" : node.Path[..dot];
                foreach (var (op, json) in new[] {
                    ("replace", Json("replace", null, node.Path, own)),
                    ("remove", Json("remove", null, node.Path, null)),
                    ("add", Json("add", parent, null, own)),
                }) {
                    rows.Add(($"{doc}|{node.Kind}|{node.Path}|{op}", Result(text, PolyEdit.Apply(text, json), op == "replace")));
                }
            }
        }

        return rows;
    }

    internal static List<(string Key, string Result)> ReadRows() =>
        [.. File.ReadAllLines(Path.Combine(GuideBlocks.RepoRoot(), FilePath)).Where(l => l.StartsWith("P\t", StringComparison.Ordinal))
            .Select(l => l.Split('\t')).Select(f => (f[1], f[2]))];

    /// <summary>Every frozen row must equal the recomputed result; pure so the self-test can mutate a copy.</summary>
    internal static List<string> Compare(IReadOnlyList<(string Key, string Result)> frozen, IReadOnlyList<(string Key, string Result)> actual) {
        var failures = new List<string>();
        var now = actual.ToDictionary(r => r.Key, r => r.Result, StringComparer.Ordinal);
        foreach (var (key, result) in frozen) {
            if (!now.TryGetValue(key, out var seen))
                failures.Add($"{key}: no longer sampled");
            else if (seen != result)
                failures.Add($"{key}\n    expected {result}\n    actual   {seen}");
        }
        foreach (var key in now.Keys.Except(frozen.Select(f => f.Key)))
            failures.Add($"{key}: sampled but not frozen");
        return failures;
    }

    [Test]
    public async Task Regenerate_WritesTheEditResults() {
        if (Environment.GetEnvironmentVariable("POLY_REGEN_EDIT_SPLICE") is null)
            return;
        var lines = new List<string> {
            "# G-26b: PolyEdit results for the first node of each kind per valid document. P doc|kind|path|op<TAB>result. See EditSpliceBaselineTests.",
        };
        lines.AddRange(ComputeRows().Select(r => $"P\t{r.Key}\t{r.Result}"));
        File.WriteAllLines(Path.Combine(GuideBlocks.RepoRoot(), FilePath), lines);
        await Assert.That(lines.Count).IsGreaterThan(100);
    }

    [Test]
    public async Task EditResults_MatchTheFrozenBaseline() =>
        await Assert.That(Compare(ReadRows(), ComputeRows())).IsEmpty();

    [Test]
    public async Task Sample_CoversEveryKindOfNodeAndEveryOp() {
        var rows = ReadRows();
        var kinds = rows.Select(r => r.Key.Split('|')[1]).Distinct().ToList();

        await Assert.That(rows.Count).IsGreaterThan(800);
        foreach (var kind in new[] { "uses", "entity", "enum", "value", "contract", "bind", "property", "navigation", "stage", "action", "policy" })
            await Assert.That(kinds).Contains(kind);
        foreach (var op in new[] { "replace", "remove", "add" })
            await Assert.That(rows.Count(r => r.Key.EndsWith("|" + op, StringComparison.Ordinal))).IsGreaterThan(100);
    }

    /// <summary>
    /// The invariant the baseline first showed false: replacing a node with its own text, taken from the node start, is byte-identical.
    /// (Probe: <c>Patron.Active</c> in <c>demo/Poly.RestApi/library.poly</c> taken from the LINE start was identical; taken from the node
    /// start it grew by 36 characters, because <c>Reindent</c> took the common indent over the first line too.)
    /// </summary>
    [Test]
    public async Task ReplaceWithOwnText_FromTheNodeStart_IsByteIdenticalForEverySampledNode() {
        var failures = ComputeRows().Where(r => r.Key.EndsWith("|replace", StringComparison.Ordinal) && r.Result.Contains("identical=N", StringComparison.Ordinal))
            .Select(r => r.Key).ToList();

        await Assert.That(failures).IsEmpty();
    }

    // ── Reindent: the first line sits at the node's own position (PolyEdit.Reindent, skipFirst) ──

    private const string StageDocument = "domain A\nX: entity {\n  S: stage {\n    Go: action { }\n  }\n  N: Text\n}\n";

    private static string ReplaceStage(string document, string dsl) =>
        PolyEdit.Apply(document, JsonSerializer.Serialize(new[] { new { op = "replace", path = "X.S", dsl } })).Text!;

    [Test]
    public async Task Reindent_ASnippetWrittenFromColumnZeroWithADeeperBody_LandsAtTheNodesIndent() =>
        await Assert.That(ReplaceStage(StageDocument, "S: stage {\n  Go: action { }\n}")).IsEqualTo(StageDocument);

    [Test]
    public async Task Reindent_AOneLineSnippet_LandsAtTheNodesIndent() =>
        await Assert.That(ReplaceStage(StageDocument, "S: stage { }")).IsEqualTo("domain A\nX: entity {\n  S: stage { }\n  N: Text\n}\n");

    [Test]
    public async Task Reindent_ContinuationLinesAllDeeperThanTheNodesIndent_AreKeptWhereTheyAre() =>
        await Assert.That(ReplaceStage(StageDocument, "S: stage {\n        Go: action { }\n        }"))
            .IsEqualTo("domain A\nX: entity {\n  S: stage {\n        Go: action { }\n        }\n  N: Text\n}\n");

    [Test]
    public async Task Reindent_BlankLinesInTheSnippet_StayBlank() =>
        await Assert.That(ReplaceStage(StageDocument, "S: stage {\n\n    Go: action { }\n  }"))
            .IsEqualTo("domain A\nX: entity {\n  S: stage {\n\n    Go: action { }\n  }\n  N: Text\n}\n");

    [Test]
    public async Task Reindent_CrLf_KeepsTheDocumentsLineEndingsAndIsIdentical() {
        var crlf = StageDocument.Replace("\n", "\r\n", StringComparison.Ordinal);
        var node = DslOutline.Build(crlf).All().First(n => n.Path == "X.S");

        await Assert.That(ReplaceStage(crlf, crlf[node.StartOffset..node.EndOffset])).IsEqualTo(crlf);
    }

    [Test]
    public async Task Reindent_ASnippetTakenFromTheLineStart_IsStillIdentical() {
        var node = DslOutline.Build(StageDocument).All().First(n => n.Path == "X.S");
        var lineStart = StageDocument.LastIndexOf('\n', node.StartOffset - 1) + 1;

        await Assert.That(ReplaceStage(StageDocument, StageDocument[lineStart..node.EndOffset])).IsEqualTo(StageDocument);
    }

    // ── closing round: the rule in doc coordinates (C1 hanging indent, C2 column-0 line) ──

    private static string ReplaceAt(string document, string path, string dsl) =>
        PolyEdit.Apply(document, JsonSerializer.Serialize(new[] { new { op = "replace", path, dsl } })).Text!;

    private static string OwnText(string document, string path) {
        var node = DslOutline.Build(document).All().First(n => n.Path == path);
        return document[node.StartOffset..node.EndOffset];
    }

    private static async Task AssertIdentical(string document, string path) {
        var once = ReplaceAt(document, path, OwnText(document, path));
        await Assert.That(once).IsEqualTo(document);
        await Assert.That(ReplaceAt(once, path, OwnText(once, path))).IsEqualTo(document).Because("a second replace must not grow it");
    }

    /// <summary>Every addressable node of every valid document, replaced with its own text from the node start, comes back byte-identical.</summary>
    [Test]
    public async Task ReplaceWithOwnText_IsByteIdenticalForEveryNodeOfEveryShippedDocument() {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var nodes = 0;
        var failures = new List<string>();
        foreach (var (doc, text) in ParityDocuments.All) {
            var outline = DslOutline.Build(text);
            foreach (var node in outline.All()) {
                if (node.Path.Length == 0 || outline.Find(node.Path, out _, out _) is not { } found || !ReferenceEquals(found, node))
                    continue;
                nodes++;
                var result = PolyEdit.Apply(text, JsonSerializer.Serialize(new[] { new { op = "replace", path = node.Path, dsl = text[node.StartOffset..node.EndOffset] } }));
                if (!result.Ok || result.Text != text)
                    failures.Add($"{doc}|{node.Kind}|{node.Path}");
            }
        }

        Console.WriteLine($"every-node sweep: {nodes} nodes in {clock.ElapsedMilliseconds} ms, {failures.Count} not identical");
        await Assert.That(nodes).IsGreaterThan(1000);
        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task Reindent_AHangingIndentFunctionAtTheTopLevel_IsIdentical() =>
        await AssertIdentical("domain A\nfunction Sq(a: Number): Number =\n    a * a\n", "Sq");

    [Test]
    public async Task Reindent_AHangingIndentMemberNestedInAnEntity_IsIdentical() =>
        await AssertIdentical("domain A\nX: entity {\n  N: Number\n      range(1, 5)\n  M: Text\n}\n", "X.N");

    [Test]
    public async Task Reindent_AColumnZeroCommentInsideANestedNode_IsIdenticalAndDoesNotGrow() =>
        await AssertIdentical("domain A\nX: entity {\n  S: stage {\n    Go: action { }\n// note\n    Done: action { }\n  }\n}\n", "X.S");

    /// <summary>The DSL allows a newline inside a string literal. Its content is not code: replace must not re-indent it.</summary>
    [Test]
    public async Task Reindent_ALineInsideAMultiLineStringLiteral_IsNotReindented() =>
        await AssertIdentical("domain A\nX: entity {\n  N: Text pattern(\"a\nb\")\n  M: Text\n}\n", "X.N");

    [Test]
    public async Task Reindent_TabIndentedNodes_AreIdentical() =>
        await AssertIdentical("domain A\nX: entity {\n\tS: stage {\n\t\tGo: action { }\n\t}\n}\n", "X.S");

    [Test]
    public async Task Reindent_ASnippetAuthoredFromColumnZeroForANestedNode_LandsBodyAtTwiceTheIndentAndTheBraceAtIt() =>
        await Assert.That(ReplaceAt(StageDocument, "X.S", "S: stage {\n  entry { }\n}"))
            .IsEqualTo("domain A\nX: entity {\n  S: stage {\n    entry { }\n  }\n  N: Text\n}\n");

    [Test]
    public async Task SelfTest_OneEditResultChanged_FailsTheBaseline() {
        var frozen = ReadRows();
        var mutated = frozen.ToList();
        var index = mutated.FindIndex(r => r.Result.StartsWith("ok", StringComparison.Ordinal));
        mutated[index] = (mutated[index].Key, mutated[index].Result.Replace("sha=", "sha=0", StringComparison.Ordinal));
        var failures = Compare(mutated, ComputeRows());

        await Assert.That(failures).IsNotEmpty();
        await Assert.That(failures[0]).Contains(mutated[index].Key);
    }

    [Test]
    public async Task SelfTest_ASampledNodeWithNoFrozenRow_FailsTheBaseline() {
        var frozen = ReadRows();
        var failures = Compare(frozen.Skip(1).ToList(), ComputeRows());

        await Assert.That(failures.Any(f => f.Contains("sampled but not frozen", StringComparison.Ordinal))).IsTrue();
    }
}