using System.Globalization;

using Poly.Tests.Polyc;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// A domain in the shared golden corpus, located by its repo-relative
/// <paramref name="Path"/> so the same catalog works from any test worktree.
/// </summary>
public sealed record GoldenDomain(string Name, string Path, bool Compiles) {
    /// <summary>Absolute path of the domain on disk.</summary>
    public string FullPath => System.IO.Path.Combine(GoldenDomains.RepoRoot, Path);

    /// <summary>The domain source.</summary>
    public string Poly => File.ReadAllText(FullPath);
}

/// <summary>
/// One catalog for every golden domain: <see cref="Compilable"/> is the frozen
/// set <c>EmitGoldenTests</c> snapshots, and <see cref="All"/> adds any repro
/// dropped in <c>Poly.Tests/Corpus/swarm/</c>.
/// </summary>
public static class GoldenDomains {
    /// <summary>Repository root, the base every <see cref="GoldenDomain.Path"/> is relative to.</summary>
    public static string RepoRoot { get; } = PolycTestSupport.RepoRoot();

    /// <summary>Where unreproduced-in-suite bugs are parked; globbed, never listed.</summary>
    public static string SwarmDirectory { get; } =
        System.IO.Path.Combine(RepoRoot, "Poly.Tests", "Corpus", "swarm");

    // The set EmitGoldenTests emits. Editing this list moves the goldens, which is
    // why GoldenDomainsTests pins it.
    private static readonly string[] EmitGoldenPaths = [
        "docs/probes/fleet-eval/09-transport/warehouse.poly",
        "docs/probes/fleet-eval/09-transport/orders.poly",
        "docs/probes/fleet-eval/09-transport/clinic.poly",
        "docs/probes/fleet-eval/12-mcp/mcp-library.poly",
        "docs/probes/dogfood/university.poly",
        "docs/probes/dogfood/crm.poly",
        "docs/probes/dogfood/hotel.poly",
        "docs/probes/dogfood/simulate-create-type.poly",
        "docs/probes/dogfood/simulate-create-in.poly",
        "docs/probes/dogfood/simulate-create-create-in.poly",
    ];

    /// <summary>The domains that have an EmitGolden snapshot.</summary>
    public static IReadOnlyList<GoldenDomain> Compilable { get; } =
        [.. EmitGoldenPaths.Select(p => new GoldenDomain(
            System.IO.Path.GetFileNameWithoutExtension(p), p, Compiles: true))];

    /// <summary>Every corpus domain: <see cref="Compilable"/> plus <see cref="Swarm()"/>.</summary>
    public static IReadOnlyList<GoldenDomain> All { get; } = [.. Compilable, .. Swarm()];

    /// <summary>
    /// Every <c>*.poly</c> under <see cref="SwarmDirectory"/>, ordered by name.
    /// A directory glob, so a dropped file needs no code change.
    /// </summary>
    public static IReadOnlyList<GoldenDomain> Swarm() =>
        Directory.Exists(SwarmDirectory)
            ? [.. Directory.GetFiles(SwarmDirectory, "*.poly")
                .Order(StringComparer.Ordinal)
                .Select(p => new GoldenDomain(
                    System.IO.Path.GetFileNameWithoutExtension(p),
                    System.IO.Path.GetRelativePath(RepoRoot, p).Replace('\\', '/'),
                    Compiles: false))]
            : [];

    /// <summary>The swarm repro named <c>&lt;issue&gt;.poly</c>.</summary>
    public static GoldenDomain Swarm(int issue) {
        var name = issue.ToString(CultureInfo.InvariantCulture);
        return Swarm().SingleOrDefault(d => d.Name == name)
            ?? throw new KeyNotFoundException($"No {name}.poly in {SwarmDirectory}.");
    }
}