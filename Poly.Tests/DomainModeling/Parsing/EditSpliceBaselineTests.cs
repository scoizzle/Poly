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
/// <para>No invariant is asserted. "Replace with its own text is byte-identical" was run over every sampled node and does NOT hold:
/// 69 of the sampled stage and action nodes grow (first counter-example: <c>demo/Poly.RestApi/library.poly</c>, stage
/// <c>Patron.Active</c>, +36 characters: a multi-line node's continuation lines are indented again). The rows freeze that result.</para>
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

    [Test]
    public async Task ReplaceWithOwnText_IsNotByteIdenticalForEveryNode_SoNoInvariantIsAsserted() {
        var counterExample = ReadRows().FirstOrDefault(r => r.Key.EndsWith("|replace", StringComparison.Ordinal) && r.Result.Contains("identical=N", StringComparison.Ordinal));

        await Assert.That(counterExample.Key).IsEqualTo("demo/Poly.RestApi/library.poly|stage|Patron.Active|replace");
    }

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