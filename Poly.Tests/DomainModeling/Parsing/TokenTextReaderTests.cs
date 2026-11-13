using Poly.Tests.Docs;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// Every consumer reads a DSL token through the span API (<c>Span</c>, <c>TextEquals</c>, <c>Name()</c>,
/// <c>Unescape()</c>) or one of two helpers in <c>DslTokenText</c>: <c>Shown</c> for message text, and <c>Word</c>
/// for a decision on a token of unknown kind, which reads only an identifier or a keyword. G-13c removed
/// <c>LegacyText</c>, the by-name quirk that read a quoted string as a word, so no call may come back.
/// </summary>
public sealed class TokenTextReaderTests {
    private static IEnumerable<string> ProductionSources(string root) {
        foreach (var dir in new[] { "Poly", "Poly.Mcp", "src" }) {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, dir), "*.cs", SearchOption.AllDirectories)) {
                var rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                if (rel.Contains("/bin/", StringComparison.Ordinal) || rel.Contains("/obj/", StringComparison.Ordinal))
                    continue;
                yield return rel;
            }
        }
    }

    private static int Count(string text, string needle) {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    [Test]
    public async Task LegacyText_NoProductionSourceReadsIt() {
        var root = GuideBlocks.RepoRoot();
        var found = new List<string>();
        foreach (var rel in ProductionSources(root)) {
            if (Count(File.ReadAllText(Path.Combine(root, rel)), "LegacyText") > 0)
                found.Add(rel);
        }

        await Assert.That(found).IsEmpty();
    }
}
