using Poly.Tests.Docs;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// After G-13a every consumer reads a DSL token through the span API (<c>Span</c>, <c>TextEquals</c>,
/// <c>Name()</c>, <c>Unescape()</c>) or one of two helpers in <c>DslTokenText</c>: <c>Shown</c> for message
/// text, and <c>LegacyText</c> for a DECISION on a token whose kind the code does not know, where a string
/// literal still reads as its decoded value (<c>X: "enum"</c> classifies as an enum). The decision sites
/// are pinned here, so a new one is a reviewed change and the quirk fix can find every one.
/// </summary>
public sealed class TokenTextReaderTests {
    private static readonly Dictionary<string, int> LegacyTextCallSites = new(StringComparer.Ordinal) {
        ["Poly/DomainModeling/Language/DomainResolver.Entities.cs"] = 4,
        ["Poly/DomainModeling/Language/DomainResolver.TopLevel.cs"] = 1,
        ["Poly/DomainModeling/Language/DslOutline.cs"] = 6,
        ["Poly/DomainModeling/Language/PolyEdit.cs"] = 1,
        ["src/Poly.Compile/PolyCompiler.cs"] = 1,
    };

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
    public async Task LegacyText_DecisionSites_ArePinned() {
        var root = GuideBlocks.RepoRoot();
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rel in ProductionSources(root)) {
            var count = Count(File.ReadAllText(Path.Combine(root, rel)), "DslTokenText.LegacyText(");
            if (count > 0)
                found[rel] = count;
        }

        await Assert.That(found.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"))
            .IsEquivalentTo(LegacyTextCallSites.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"));
    }
}