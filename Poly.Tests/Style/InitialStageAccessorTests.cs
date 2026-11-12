using System.Text.RegularExpressions;

using Poly.DomainModeling.Ontology;
using Poly.Tests.Polyc;

namespace Poly.Tests.Style;

/// <summary>
/// "Which stage does a new instance start in" has one answer: the first declared stage, or none.
/// <see cref="EntityStages.InitialStage"/> is that one answer. These tests hold the contract (A)
/// and keep the question from being re-answered by indexing at each call site (B).
/// </summary>
public partial class InitialStageAccessorTests {
    /// <summary>The one file allowed to answer the question.</summary>
    private const string AccessorPath = "Poly/DomainModeling/Ontology/EntityStages.cs";

    /// <summary>
    /// Permitted direct reads, each with the reason it is not an initial-stage question. An entry
    /// excuses a match only when the match's own source line contains <see cref="Text"/>, so the
    /// receiver is part of what is permitted: another receiver in the same file is still a finding.
    /// </summary>
    private static readonly (string File, string Text, string Reason)[] Allowed = [
        ("Poly/DomainModeling/Lowering/DomainToCSharpExporter.Actions.cs", "group.Stages[0]",
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
            await Assert.That(File.Exists(path)).IsTrue()
                .Because($"allow-list entry {entry.File} must exist");
            await Assert.That((await File.ReadAllTextAsync(path)).Contains(entry.Text, StringComparison.Ordinal))
                .IsTrue()
                .Because($"allow-list entry {entry.File} must still contain '{entry.Text}' ({entry.Reason})");
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
    /// Every source file counts, whatever its root: a renamed or new root cannot go silent.
    /// <c>Poly.Tests</c> is the one exclusion — it legitimately reads <c>Stages[0]</c> when it
    /// asserts about a parsed domain. Build output is judged on the relative path.
    /// </summary>
    private static bool IsExcluded(string relativePath) =>
        relativePath.StartsWith("Poly.Tests/", StringComparison.Ordinal)
        || relativePath.Contains("/bin/", StringComparison.Ordinal)
        || relativePath.Contains("/obj/", StringComparison.Ordinal);

    /// <summary>The direct first-stage reads in one file that no allow-list entry excuses.</summary>
    internal static IEnumerable<string> Offenders(string relativePath, string text) {
        foreach (Match match in FirstStageRead().Matches(text)) {
            var lineNumber = LineOf(text, match.Index);
            var line = LineText(text, lineNumber);
            var excused = AllowedReadsFor(relativePath)
                .Any(entry => line.Contains(entry.Text, StringComparison.Ordinal));
            if (excused)
                continue;
            yield return $"{relativePath}:{lineNumber}: {match.Value.Trim()}";
        }
    }

    /// <summary>Allow-list entries for one file, matched on that file's relative path.</summary>
    private static IEnumerable<(string Text, string Reason)> AllowedReadsFor(string relativePath) =>
        Allowed.Where(entry => entry.File == relativePath).Select(entry => (entry.Text, entry.Reason));

    private static int LineOf(string text, int index) {
        var line = 1;
        for (var i = 0; i < index; i++)
            if (text[i] == '\n')
                line++;
        return line;
    }

    private static string LineText(string text, int lineNumber) {
        var start = 0;
        for (var line = 1; line < lineNumber; line++)
            start = text.IndexOf('\n', start) + 1;
        var end = text.IndexOf('\n', start);
        return end < 0 ? text[start..] : text[start..end];
    }

    /// <summary>
    /// A read of the first stage: indexing, or any of the "first element" LINQ shapes. Predicate
    /// overloads (<c>FirstOrDefault(s =&gt; …)</c>, <c>Last(s =&gt; …)</c>) find a named stage, not
    /// the initial one, so the argument-less forms only.
    /// </summary>
    [GeneratedRegex(
        @"\s*\??\.\s*Stages\s*(?:\[\s*0\s*\]|\??\.\s*(?:First(?:OrDefault)?\s*\(\s*\)|Single(?:OrDefault)?\s*\(\s*\)|Last(?:OrDefault)?(?!\w)(?![ \t]*\((?![ \t]*\)))(?:\s*\(\s*\))?|ElementAt\s*\(\s*0\s*\)|Skip\s*\(\s*1\s*\)|Take\s*\(\s*1\s*\)))",
        RegexOptions.Compiled)]
    private static partial Regex FirstStageRead();
}