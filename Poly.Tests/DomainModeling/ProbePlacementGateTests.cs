namespace Poly.Tests.DomainModeling;

/// <summary>
/// F4: live *.poly probes stay under docs/probes/ (or archived probes-2026-08).
/// Repo-root probes/ must not return after 66f8eeb0.
/// </summary>
public class ProbePlacementGateTests {
    private static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir, "Poly.sln"))
                || File.Exists(Path.Combine(dir, "docs/CORE.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root from " + AppContext.BaseDirectory);
    }

    [Test]
    public async Task RepoRoot_ProbesDirectory_MustNotExist() {
        var root = FindRepoRoot();
        var probesRoot = Path.Combine(root, "probes");
        await Assert.That(Directory.Exists(probesRoot)).IsFalse();
    }

    [Test]
    public async Task StrayRepoRootProbesPoly_MustBeEmpty() {
        var root = FindRepoRoot();
        var allowedPrefixes = new[] {
            Path.Combine(root, "docs", "probes") + Path.DirectorySeparatorChar,
            Path.Combine(root, "docs", "plans", "archive", "probes-2026-08") + Path.DirectorySeparatorChar,
        };

        // Flags *.poly only under repo-root probes/ (live fixtures belong under
        // docs/probes/). RepoRoot_ProbesDirectory_MustNotExist owns the directory check.
        var stray = new List<string>();
        var rootProbes = Path.Combine(root, "probes");
        if (Directory.Exists(rootProbes)) {
            stray.AddRange(Directory.EnumerateFiles(rootProbes, "*.poly", SearchOption.AllDirectories));
        }

        // Also flag *.poly directly under unexpected sibling of docs/probes that
        // looks like a probe drop (none expected).
        foreach (var poly in Directory.EnumerateFiles(root, "*.poly", SearchOption.AllDirectories)) {
            var full = Path.GetFullPath(poly);
            if (allowedPrefixes.Any(p => full.StartsWith(p, StringComparison.Ordinal)))
                continue;
            // Ignore non-probe trees (examples, packs, generated, node_modules, etc.)
            var rel = Path.GetRelativePath(root, full).Replace('\\', '/');
            if (rel.StartsWith("docs/probes/", StringComparison.Ordinal)
                || rel.StartsWith("docs/plans/archive/probes-2026-08/", StringComparison.Ordinal))
                continue;
            if (rel.StartsWith("probes/", StringComparison.Ordinal))
                stray.Add(rel);
        }

        await Assert.That(stray).IsEmpty();
    }

    [Test]
    public async Task SimulateCreateProbes_LiveUnderDocsProbesDogfood() {
        var root = FindRepoRoot();
        foreach (var name in new[] {
            "simulate-create-type.poly",
            "simulate-create-in.poly",
            "simulate-create-create-in.poly",
        }) {
            var path = Path.Combine(root, "docs", "probes", "dogfood", name);
            await Assert.That(File.Exists(path)).IsTrue();
        }
    }
}
