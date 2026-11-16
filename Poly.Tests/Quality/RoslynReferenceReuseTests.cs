namespace Poly.Tests.Quality;

/// <summary>
/// Every Roslyn compilation in the suite takes its references from <see cref="RoslynReferences"/>. A reference set built
/// per call re-reads every trusted platform assembly (about 270 files) for each compilation and shares no metadata
/// between them: on 2026-11-16 that cost the full suite 6.5 GB of peak RSS against 2.7 GB with one shared set, and the
/// constant-fold oracle 67 s against 10 s.
/// </summary>
public class RoslynReferenceReuseTests {
    [Test]
    public async Task NoTestBuildsItsOwnPlatformReferenceSet() {
        var root = Path.Combine(GoldenDomains.RepoRoot, "Poly.Tests");
        var helper = Path.Combine(root, "TestHelpers", "RoslynReferences.cs");
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => path != helper && path != Path.Combine(root, "Quality", "RoslynReferenceReuseTests.cs"))
            .Where(static path => File.ReadAllText(path).Contains("TRUSTED_PLATFORM_ASSEMBLIES", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(offenders).IsEmpty();
    }
}