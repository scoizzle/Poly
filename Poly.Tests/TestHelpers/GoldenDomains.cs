using System.Globalization;

using Poly.Tests.Polyc;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// A domain in the shared golden corpus, located by its repo-relative
/// <paramref name="Path"/> so the same catalog works from any test worktree.
/// <paramref name="Compiles"/> is true when the C10 oracle lowers and executes the domain;
/// false means the domain is recorded with a <see cref="SkipReason"/>.
/// </summary>
public sealed record GoldenDomain(string Name, string Path, bool Compiles) {
    /// <summary>Why the C10 oracle does not lower this domain; null when it does.</summary>
    public string? SkipReason { get; init; }

    /// <summary>Absolute path of the domain on disk.</summary>
    public string FullPath => System.IO.Path.Combine(GoldenDomains.RepoRoot, Path);

    /// <summary>The domain source.</summary>
    public string Poly => File.ReadAllText(FullPath);
}

/// <summary>
/// One catalog for every golden domain: <see cref="Compilable"/> is the frozen
/// set <c>EmitGoldenTests</c> snapshots; <see cref="Lowerable"/> adds the probes the
/// C10 oracle lowers; <see cref="Skipped"/> records the probes it does not, with reasons;
/// and <see cref="All"/> adds any repro dropped in <c>Poly.Tests/Corpus/swarm/</c>.
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

    // Probes outside the emit set that the C10 oracle lowers and executes.
    private static readonly string[] ProbeOnlyPaths = [
        "demo/live/checkout.poly",
        "demo/live/domain.poly",
        "docs/probes/dogfood/entry-transition-in-first-stage.poly",
        "docs/probes/dogfood/peer-tracking-transition.poly",
        "docs/probes/grep-2026-10-17/grep.poly",
        "docs/probes/scratch-2026-10-14/banking.poly",
        "docs/probes/scratch-2026-10-14/fleet.poly",
        "docs/probes/scratch-2026-10-14/gym.poly",
        "docs/probes/scratch-2026-10-14/insurance.poly",
        "docs/probes/scratch-2026-10-14/logistics.poly",
        "docs/probes/scratch-2026-10-14/payroll.poly",
        "docs/probes/scratch-2026-10-14/roastery.poly",
        "docs/probes/smoke/smoke.poly",
    ];

    /// <summary>The domains the C10 oracle lowers and executes: <see cref="Compilable"/> plus the probe-only set.</summary>
    public static IReadOnlyList<GoldenDomain> Lowerable { get; } = [
        .. Compilable,
        .. ProbeOnlyPaths.Select(p => new GoldenDomain(
            System.IO.Path.GetFileNameWithoutExtension(p), p, Compiles: true)),
    ];

    /// <summary>Probes the C10 oracle does not lower; each reason is re-derived by the oracle's freshness test.</summary>
    public static IReadOnlyList<GoldenDomain> Skipped { get; } = [
        new("nested-invoke-type-mismatch", "docs/probes/dogfood/nested-invoke-type-mismatch.poly", Compiles: false) {
            SkipReason = "type error by design",
        },
        new("restaurant", "docs/probes/scratch-2026-10-14/restaurant.poly", Compiles: false) {
            SkipReason = "stores a domain value type as an entity property (DMSTR006); does not analyze",
        },
    ];

    /// <summary>Every corpus domain: <see cref="Lowerable"/>, <see cref="Skipped"/> and <see cref="Swarm()"/>.</summary>
    public static IReadOnlyList<GoldenDomain> All { get; } = [.. Lowerable, .. Skipped, .. Swarm()];

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
                    Compiles: false) { SkipReason = "parked repro; the oracle does not lower it" })]
            : [];

    /// <summary>The swarm repro named <c>&lt;issue&gt;.poly</c>.</summary>
    public static GoldenDomain Swarm(int issue) {
        var name = issue.ToString(CultureInfo.InvariantCulture);
        return Swarm().SingleOrDefault(d => d.Name == name)
            ?? throw new KeyNotFoundException($"No {name}.poly in {SwarmDirectory}.");
    }
}