using System.Text.RegularExpressions;

namespace Poly.Tests.Docs;

/// <summary>
/// Every relative Markdown link in **every** Markdown file in the repository points at a file or
/// directory that exists: the root README and AGENTS.md, docs/, .github/, the module READMEs under
/// `Poly/`, the DSL guide under `Poly.Mcp/`, the packs under `src/`, and the demos. URLs, mail
/// links and in-page anchors are skipped, as are links inside code fences and inline code; for the
/// rest only the path before '#' is checked.
/// <para>
/// The scope is discovered, not listed: a hand-written list of folders is how `Poly/*/README.md`
/// went unchecked for weeks (#390).
/// </para>
/// </summary>
public partial class RelativeDocLinkTests {
    [Test]
    public async Task RelativeLinks_EveryMarkdownFile_Resolve() {
        var root = RepoRoot();
        var files = Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(root, file))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();
        await Assert.That(files.Count).IsGreaterThan(100)
            .Because("the sweep should see the whole repository, not a slice of it");
        var broken = files.SelectMany(file => BrokenLinks(root, file)).ToList();
        await Assert.That(string.Join("\n", broken)).IsEqualTo(string.Empty);
    }

    private static bool IsBuildOutput(string root, string file) {
        var relative = Path.GetRelativePath(root, file);
        return relative.Split(Path.DirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj" or ".git" or "node_modules" or ".vs");
    }

    private static IEnumerable<string> BrokenLinks(string root, string file) {
        var directory = Path.GetDirectoryName(file)!;
        var inFence = false;
        var number = 0;
        foreach (var line in File.ReadLines(file)) {
            number++;
            if (Fence().IsMatch(line)) {
                inFence = !inFence;
                continue;
            }
            if (inFence)
                continue;
            foreach (Match match in Link().Matches(InlineCode().Replace(line, string.Empty))) {
                var target = match.Groups[1].Value;
                if (Scheme().IsMatch(target) || target.StartsWith('#'))
                    continue;
                var path = target.Split('#', 2)[0];
                var resolved = Path.GetFullPath(Path.Combine(directory, path));
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                    yield return $"{Path.GetRelativePath(root, file)}:{number}: {target}";
            }
        }
    }

    [GeneratedRegex("""\[[^\]]*\]\(([^)\s]+)(?:\s+"[^"]*")?\)""")]
    private static partial Regex Link();

    [GeneratedRegex(@"^\s*(```|~~~)")]
    private static partial Regex Fence();

    [GeneratedRegex("`[^`]*`")]
    private static partial Regex InlineCode();

    [GeneratedRegex("^[a-z][a-z0-9+.-]*:")]
    private static partial Regex Scheme();

    private static string RepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))
                && Directory.Exists(Path.Combine(dir.FullName, "docs")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root with AGENTS.md and docs/ not found.");
    }
}