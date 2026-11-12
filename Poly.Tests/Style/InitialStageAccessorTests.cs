using System.Text.RegularExpressions;
using Poly.DomainModeling.Ontology;
using Poly.Tests.Polyc;

namespace Poly.Tests.Style;

/// <summary>
/// "Which stage does a new instance start in" has one answer: the first declared stage, or none.
/// <see cref="Poly.DomainModeling.Ontology.EntityStages.InitialStage"/> is that one answer. These
/// tests hold the contract (A) and keep the question from being re-answered by indexing at each
/// call site (B).
/// </summary>
public partial class InitialStageAccessorTests {
    /// <summary>The one file allowed to answer the question.</summary>
    private const string AccessorPath = "Poly/DomainModeling/Ontology/EntityStages.cs";

    /// <summary>
    /// Source roots that hold production code. <c>Poly.Tests</c> is excluded: it legitimately reads
    /// <c>Stages[0]</c> when it asserts about a parsed domain.
    /// </summary>
    private static readonly string[] ScanRoots =
        ["Poly", "src", "Poly.Mcp", "Poly.Runtime", "Poly.Demo", "demo"];

    /// <summary>
    /// Permitted direct reads, each with the reason it is not an initial-stage question. The entry is
    /// matched as a substring of the file text, and the file must exist and still contain it.
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

        var allowed = Allowed.ToDictionary(
            a => a.File,
            a => a.Text,
            StringComparer.Ordinal);
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
        foreach (var scope in ScanRoots) {
            var directory = Path.Combine(root, scope.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory))
                continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)) {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (IsBuildOutput(file) || relative == AccessorPath)
                    continue;
                scanned++;
                var text = await File.ReadAllTextAsync(file);
                foreach (Match match in FirstStageRead().Matches(text)) {
                    if (allowed.TryGetValue(relative, out var permitted)
                        && permitted.Contains(match.Value, StringComparison.Ordinal))
                        continue;
                    offenders.Add($"{relative}:{LineOf(text, match.Index)}: {match.Value.Trim()}");
                }
            }
        }

        await Assert.That(scanned).IsGreaterThanOrEqualTo(MinScannedFiles);
        await Assert.That(string.Join(Environment.NewLine, offenders)).IsEmpty()
            .Because("read the initial stage through EntityStages.InitialStage(this Entity)");
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
    }

    private static bool IsBuildOutput(string file) =>
        file.Contains("/bin/") || file.Contains("/obj/")
        || file.Contains("\\bin\\") || file.Contains("\\obj\\");

    private static int LineOf(string text, int index) {
        var line = 1;
        for (var i = 0; i < index; i++)
            if (text[i] == '\n')
                line++;
        return line;
    }

    /// <summary>
    /// A read of the first stage: indexing, or any of the "first element" LINQ shapes. Predicate
    /// overloads (<c>FirstOrDefault(s =&gt; …)</c>) find a named stage, not the initial one, so the
    /// argument-less forms only.
    /// </summary>
    [GeneratedRegex(
        @"\s*\??\.\s*Stages\s*(?:\[\s*0\s*\]|\??\.\s*(?:First(?:OrDefault)?\s*\(\s*\)|Single(?:OrDefault)?\s*\(\s*\)|Last(?:OrDefault)?(?!\w)(?:\s*\(\s*\))?|ElementAt\s*\(\s*0\s*\)|Skip\s*\(\s*1\s*\)|Take\s*\(\s*1\s*\)))",
        RegexOptions.Compiled)]
    private static partial Regex FirstStageRead();
}
