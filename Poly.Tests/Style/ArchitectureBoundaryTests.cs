namespace Poly.Tests.Style;

/// <summary>
/// I8: the layers that state meaning do not name execution or reflection facts. <c>Vm/**</c> is the host that compiles
/// to <c>System.Linq.Expressions</c> and <c>CommonLanguageRuntime/**</c> is the host that reflects; everything that
/// states meaning asks the platform instead. An exception here is a removing row in #972, never a permanent allowance.
/// </summary>
public class ArchitectureBoundaryTests {
    /// <summary>Substrings, so a fully qualified name or an alias is caught too, not only a plain using.</summary>
    private static readonly string[] Forbidden = [
        "System.Linq.Expressions",
        "System.Reflection"
    ];

    /// <summary>Analysis files that still name a reflection/expression fact, each with the row that removes it.</summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal) {
        ["Poly/Interpretation/Analysis/ConstantFolding/ConstantFoldingPass.cs"] =
            "invokes a declared conversion at analysis time (#954); removing row: one shared evaluator (I9)",
        ["Poly/Interpretation/Analysis/Semantics/CallSiteCatalogPass.cs"] =
            "reads a delegate's parameter shape for the catalog; removing row: I9 moves the fence to the host",
        ["Poly/Interpretation/Analysis/Semantics/AwaitPattern.cs"] =
            "reads awaiter members by reflection; removing row: I9 moves the fence to the host"
    };

    private static readonly string[] ScannedRoots = [
        "Poly/Ast",
        "Poly/Introspection",
        "Poly/Interpretation/Analysis"
    ];

    [Test]
    public async Task MeaningLayers_DoNotNameExecutionOrReflectionFacts() {
        var repo = GetRepoRoot();
        var offenders = new List<string>();
        var seenAllowlist = new HashSet<string>(StringComparer.Ordinal);

        foreach (var relativeRoot in ScannedRoots) {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(repo, relativeRoot), "*.cs", SearchOption.AllDirectories)) {
                var relative = Path.GetRelativePath(repo, file).Replace('\\', '/');
                // The host folder is where the CLR keeps its own facts.
                if (relative.StartsWith("Poly/Introspection/CommonLanguageRuntime/", StringComparison.Ordinal))
                    continue;
                var text = await File.ReadAllTextAsync(file);
                if (!Forbidden.Any(text.Contains))
                    continue;
                if (Allowed.TryGetValue(relative, out _)) {
                    seenAllowlist.Add(relative);
                    continue;
                }
                offenders.Add(relative);
            }
        }

        var reasons = Allowed.Select(entry => $"{entry.Key}: {entry.Value}");
        await Assert.That(string.Join(", ", offenders)).IsEmpty()
            .Because($"allowed exceptions, each with its removing row: {string.Join("; ", reasons)}");
        // The allowlist cannot rot: an entry that no longer offends must go with its row.
        await Assert.That(string.Join(", ", Allowed.Keys.Except(seenAllowlist))).IsEmpty();
    }

    private static string GetRepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}