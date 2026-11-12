using System.Text.RegularExpressions;

using Poly.DomainModeling.Ontology;
using Poly.Tests.Polyc;

namespace Poly.Tests.Style;

/// <summary>
/// "Which stage does a new instance start in" has one answer: the stage the entity designates with
/// <c>initial(Stage)</c>, else the first declared stage, or none. <see cref="EntityStages.InitialStage"/>
/// is that one answer. These tests hold the contract (A) and keep the question from being re-answered at
/// each call site (B).
/// </summary>
public partial class InitialStageAccessorTests {
    /// <summary>The one file allowed to answer the question.</summary>
    private const string AccessorPath = "Poly/DomainModeling/Ontology/EntityStages.cs";

    /// <summary>
    /// Permitted direct reads, each with the reason it is not an initial-stage question. An entry
    /// excuses a match only when the match's receiver is exactly <see cref="Receiver"/> at an
    /// identifier boundary, so another receiver in the same file — <c>entity</c>, or a name that merely
    /// ends in <c>group</c> — is still a finding.
    /// </summary>
    private static readonly (string File, string Receiver, string Reason)[] Allowed = [
        ("Poly/DomainModeling/Lowering/DomainToCSharpExporter.Actions.cs", "group",
            "first stage of a merged stage-action group; group.Stages is a tuple field of stage names, not Entity.Stages"),
    ];

    /// <summary>Floor so a wrong repo root cannot pass vacuously.</summary>
    private const int MinScannedFiles = 500;

    [Test]
    public async Task NoDirectFirstStageIndexingOutsideAccessor() {
        var root = PolycTestSupport.RepoRoot();
        await Assert.That(File.Exists(Path.Combine(root, "Poly", "Poly.csproj"))).IsTrue();

        foreach (var entry in Allowed) {
            var path = Path.Combine(root, entry.File.Replace('/', Path.DirectorySeparatorChar));
            await Assert.That(File.Exists(path)).IsTrue().Because($"allow-list entry {entry.File} must exist");
            await Assert.That((await File.ReadAllTextAsync(path)).Contains($"{entry.Receiver}.Stages", StringComparison.Ordinal)).IsTrue()
                .Because($"allow-list entry {entry.File} must still read {entry.Receiver}.Stages ({entry.Reason})");
        }

        var offenders = new List<string>();
        var scanned = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)) {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (IsExcluded(relative) || relative == AccessorPath)
                continue;
            scanned++;
            offenders.AddRange(Offenders(relative, await File.ReadAllTextAsync(file)));
        }

        await Assert.That(scanned).IsGreaterThanOrEqualTo(MinScannedFiles);
        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty()
            .Because("read the initial stage through EntityStages.InitialStage(this Entity)");
    }

    /// <summary>
    /// An allow-list entry exempts the receiver it names, not the whole file: a different receiver in
    /// the same file still answers the initial-stage question by hand.
    /// </summary>
    [Test]
    public async Task Offenders_AllowListedFile_StillFlagsEntityStagesZero() {
        var offenders = Offenders(
            "Poly/DomainModeling/Lowering/DomainToCSharpExporter.Actions.cs",
            "var s = entity.Stages[0];").ToList();

        await Assert.That(offenders.Count).IsEqualTo(1);
    }

    /// <summary>
    /// The receiver is the whole identifier before <c>.Stages</c>: a name that merely ends in the
    /// allow-listed one is a different receiver, and a second read on the same line is a second read.
    /// </summary>
    [Test]
    public async Task Offenders_AllowListedFile_FlagsSuffixedReceiverAndSecondRead() {
        const string file = "Poly/DomainModeling/Lowering/DomainToCSharpExporter.Actions.cs";

        var suffixed = Offenders(file, "var s = subgroup.Stages[0];").ToList();
        await Assert.That(suffixed.Count).IsEqualTo(1);

        var twice = Offenders(file, "var s = f(group.Stages[0], entity.Stages[0]);").ToList();
        await Assert.That(twice.Count).IsEqualTo(1);
    }

    /// <summary>
    /// The other shapes of the same question: a local alias, a list pattern that binds the first, a
    /// range that takes only the first, and the argument-less "first element" LINQ calls.
    /// </summary>
    [Test]
    public async Task Offenders_AliasListPatternAndRange_AreFlagged() {
        foreach (var source in new[] {
            "var stages = entity.Stages;",
            "if (entity.Stages is [var first, ..]) { }",
            "foreach (var first in entity.Stages[..1]) { }",
            "var first = entity.Stages?.Take(1);",
            "var first = entity.Stages.Skip(1);",
            "var first = entity.Stages.ElementAt(0);",
            "var first = entity.Stages.Last();",
        }) {
            var offenders = Offenders("Poly/DomainModeling/Lowering/Example.cs", source).ToList();
            await Assert.That(offenders.Count).IsEqualTo(1).Because(source);
        }
    }

    /// <summary>The whole range is every stage, not the first: it is not an initial-stage read.</summary>
    [Test]
    public async Task Offenders_WholeRange_IsNotFlagged() {
        var offenders = Offenders("Poly/DomainModeling/Lowering/Example.cs", "foreach (var s in entity.Stages[..]) { }").ToList();

        await Assert.That(offenders.Count).IsEqualTo(0);
    }

    /// <summary>A predicate lookup finds a named stage, not the initial one, and must not match.</summary>
    [Test]
    public async Task Offenders_PredicateLookups_AreNotFlagged() {
        foreach (var source in new[] {
            "var s = entity.Stages.FirstOrDefault(x => x.Name == name);",
            "var s = entity.Stages.Last(x => x.Name == name);",
            "var s = entity.Stages.Single(x => x.Name == name);",
            "var s = entity.Stages.Count;",
        }) {
            var offenders = Offenders("Poly/DomainModeling/Lowering/Example.cs", source).ToList();
            await Assert.That(offenders.Count).IsEqualTo(0).Because(source);
        }
    }

    /// <summary>
    /// The receiver need not be a bare identifier: a call, an index and a chain all reach the same
    /// list. The old receiver-less pattern caught these; a tightened one must not lose them.
    /// </summary>
    [Test]
    public async Task Offenders_NonIdentifierReceiver_IsFlagged() {
        foreach (var source in new[] {
            "var s = FindEntity(name)?.Stages[0];",
            "var s = entities[0].Stages[0];",
            "var s = domain.Types.OfType<Entity>().First().Stages.First();",
        }) {
            var offenders = Offenders("Poly/DomainModeling/Lowering/Example.cs", source).ToList();
            await Assert.That(offenders.Count).IsEqualTo(1).Because(source);
        }
    }

    [Test]
    public async Task InitialStage_ReturnsFirstDeclaredStage_AndNullWhenStageless() {
        var draft = new Stage("Draft", [], [], [], []);
        var active = new Stage("Active", [], [], [], []);
        var order = new Entity("Order", [], [], [], [draft, active]);
        var stageless = new Entity("Note", [], [], [], []);

        await Assert.That(order.InitialStage()).IsNotNull();
        await Assert.That(order.InitialStage()!.Name).IsEqualTo("Draft");
        await Assert.That(stageless.InitialStage()).IsNull();
        // Identity, not equality: TestsArtifactContributor's later-stage filter distinguishes the
        // initial stage by reference, so the accessor must hand back the declared object.
        await Assert.That(ReferenceEquals(order.InitialStage(), order.Stages[0])).IsTrue();
    }

    /// <summary>
    /// A designation resolves by name to the declared stage object. Later-stage filters compare by
    /// reference, so a copy of the stage would silently re-answer the question.
    /// </summary>
    [Test]
    public async Task Designation_IsReadByIdentity() {
        var draft = new Stage("Draft", [], [], [], []);
        var active = new Stage("Active", [], [], [], []);
        var order = new Entity("Order", [], [], [], [draft, active]) { InitialStageName = "Active" };

        await Assert.That(order.InitialStage()!.Name).IsEqualTo("Active");
        await Assert.That(ReferenceEquals(order.InitialStage(), order.Stages[1])).IsTrue();
    }

    /// <summary>
    /// Every source file counts, whatever its root: a renamed or new root cannot go silent.
    /// <c>Poly.Tests</c> is the one exclusion — it legitimately reads <c>Stages[0]</c> when it
    /// asserts about a parsed domain. Build output is judged on the relative path, so a top-level
    /// <c>bin/</c> or <c>obj/</c> (no leading slash) is excluded too.
    /// </summary>
    private static bool IsExcluded(string relativePath) =>
        relativePath.StartsWith("Poly.Tests/", StringComparison.Ordinal)
        || relativePath.StartsWith("bin/", StringComparison.Ordinal)
        || relativePath.StartsWith("obj/", StringComparison.Ordinal)
        || relativePath.Contains("/bin/", StringComparison.Ordinal)
        || relativePath.Contains("/obj/", StringComparison.Ordinal);

    /// <summary>The direct first-stage reads in one file that no allow-list entry excuses.</summary>
    internal static IEnumerable<string> Offenders(string relativePath, string text) {
        foreach (Match match in FirstStageRead().Matches(text)) {
            var receiver = match.Groups["receiver"].Value;
            if (AllowedReceiversFor(relativePath).Contains(receiver, StringComparer.Ordinal))
                continue;
            yield return $"{relativePath}:{LineOf(text, match.Index)}: {match.Value.Trim()}";
        }
    }

    /// <summary>Allow-list receivers for one file, matched on that file's relative path.</summary>
    private static IEnumerable<string> AllowedReceiversFor(string relativePath) =>
        Allowed.Where(entry => entry.File == relativePath).Select(entry => entry.Receiver);

    private static int LineOf(string text, int index) {
        var line = 1;
        for (var i = 0; i < index; i++)
            if (text[i] == '\n')
                line++;
        return line;
    }

    /// <summary>
    /// A read of the initial stage, matched over the whole file: indexing, any of the "first element"
    /// LINQ shapes, a local alias (<c>var stages = entity.Stages;</c>), a list pattern that binds the
    /// first element, and a range that takes only the first. Predicate overloads
    /// (<c>FirstOrDefault(s =&gt; …)</c>, <c>Last(s =&gt; …)</c>) find a named stage, not the initial one,
    /// so the argument-less forms only. The receiver is captured so an allow-list entry can name one;
    /// it is optional because a call, an index or a chain reaches the same list, and an absent receiver
    /// is never excused. <c>Stages[..]</c> is every stage and does not match.
    /// </summary>
    [GeneratedRegex(
        @"(?:(?<receiver>[A-Za-z_]\w*)\s*)?\??\.\s*Stages\s*(?:;\s*|\[\s*0\s*\]|\[\s*\.\.\s*1\s*\]|is\s*\[|\??\.\s*(?:First(?:OrDefault)?\s*\(\s*\)|Single(?:OrDefault)?(?![ \t]*\((?![ \t]*\)))(?:\s*\(\s*\))?|Last(?:OrDefault)?(?![ \t]*\((?![ \t]*\)))(?:\s*\(\s*\))?|ElementAt\s*\(\s*0\s*\)|Skip\s*\(\s*1\s*\)|Take\s*\(\s*1\s*\)))",
        RegexOptions.Compiled)]
    private static partial Regex FirstStageRead();
}