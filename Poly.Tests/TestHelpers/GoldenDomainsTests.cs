using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Libraries.Http;
using Poly.DomainModeling.Ontology;
using Poly.DslCompiler;
using Poly.Packs.Sqlite;

using GoldenEmitTests = Poly.Tests.DomainModeling.Compile.EmitGoldenTests;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// Guards the shared golden corpus. <c>Compilable</c> must stay exactly the set
/// <see cref="GoldenEmitTests"/> snapshots, so a golden directory can never move
/// without this test failing, and <c>All</c> must pick up anything dropped in
/// <c>Poly.Tests/Corpus/swarm/</c> without a code change.
/// </summary>
public sealed class GoldenDomainsTests {
    // Same session factory EmitGoldenTests compiles with.
    private static readonly ExtensionCatalog Catalog = ExtensionCatalog.Core
        .With(new SqliteLibrary())
        .With(new HttpLibrary());

    private static readonly string[] Seed = [.. ExtensionCatalog.ProductAuthoring, "sqlite"];

    /// <summary>The EmitGolden set, frozen: changing this list moves goldens.</summary>
    private static readonly string[] EmitGoldenSet = [
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

    /// <summary>The probes the C10 oracle lowers beyond the EmitGolden set, frozen by name.</summary>
    private static readonly string[] ProbeOnlySet = [
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

    [Test]
    public async Task All_EveryPathExists_AndParsesWithTheSessionFactory() {
        var all = GoldenDomains.All.ToList();
        await Assert.That(all.Count).IsGreaterThanOrEqualTo(GoldenDomains.Compilable.Count);

        foreach (var domain in all) {
            var fullPath = Path.Combine(GoldenDomains.RepoRoot, domain.Path);
            await Assert.That(File.Exists(fullPath)).IsTrue().Because(domain.Path);

            var poly = await File.ReadAllTextAsync(fullPath);
            await Assert.That(domain.Poly).IsEqualTo(poly).Because(domain.Path);

            var session = DomainSession.ForSource(poly, Seed, Catalog);
            var changes = new PolyDslParser(poly, session).Parse();
            await Assert.That(changes).IsNotNull().Because(domain.Path);
        }
    }

    [Test]
    public async Task Compilable_ContainsTheEmitGoldenSet() {
        var paths = GoldenDomains.Compilable.Select(d => d.Path).ToArray();

        // Exactly the set EmitGoldenTests snapshots: same members, same order.
        await Assert.That(string.Join("\n", paths))
            .IsEqualTo(string.Join("\n", EmitGoldenSet))
            .Because("Compilable is the frozen EmitGolden set; editing it moves goldens");
        await Assert.That(GoldenDomains.Compilable.All(d => d.Compiles)).IsTrue();

        // ...and EmitGoldenTests is driven by the catalog, not by its own copy.
        await Assert.That(string.Join("\n", GoldenEmitTests.SampleDomains()))
            .IsEqualTo(string.Join("\n", paths))
            .Because("EmitGoldenTests must emit from GoldenDomains.Compilable");

        // Every compilable domain has its own golden directory, named after it.
        var goldenRoot = Path.Combine(
            GoldenDomains.RepoRoot, "Poly.Tests", "DomainModeling", "Compile", "EmitGolden");
        foreach (var domain in GoldenDomains.Compilable) {
            var dir = Path.Combine(goldenRoot, domain.Name);
            await Assert.That(Directory.Exists(dir)).IsTrue().Because(domain.Name);
            await Assert.That(Directory.GetFiles(dir, "*.golden").Length).IsGreaterThan(0).Because(domain.Name);
        }
    }

    [Test]
    public async Task Swarm_DirectoryIsGlobbed_SoAnyDroppedPolyIsPickedUp() {
        var swarmDir = Path.Combine(GoldenDomains.RepoRoot, "Poly.Tests", "Corpus", "swarm");
        await Assert.That(GoldenDomains.SwarmDirectory).IsEqualTo(swarmDir);
        await Assert.That(Directory.Exists(swarmDir)).IsTrue();

        // Independent recomputation: Swarm() is exactly the *.poly on disk now.
        var onDisk = Directory.GetFiles(swarmDir, "*.poly")
            .Select(p => Path.GetRelativePath(GoldenDomains.RepoRoot, p).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);
        var fromCatalog = GoldenDomains.Swarm()
            .Select(d => d.Path)
            .Order(StringComparer.Ordinal);

        await Assert.That(string.Join("\n", fromCatalog)).IsEqualTo(string.Join("\n", onDisk));
        foreach (var domain in GoldenDomains.Swarm())
            await Assert.That(domain.SkipReason).IsNotNull().Because("a parked repro is recorded with its reason");
    }

    [Test]
    public async Task Lowerable_IsCompilablePlusTheProbeOnlySet_AndSkippedCarriesAReason() {
        var lowerable = GoldenDomains.Lowerable.Select(d => d.Path).ToHashSet(StringComparer.Ordinal);
        var skipped = GoldenDomains.Skipped.Select(d => d.Path).ToHashSet(StringComparer.Ordinal);

        await Assert.That(GoldenDomains.Compilable.All(d => lowerable.Contains(d.Path))).IsTrue()
            .Because("every EmitGolden domain is lowerable");
        await Assert.That(lowerable.Overlaps(skipped)).IsFalse()
            .Because("a domain is either lowered or skipped, never both");
        await Assert.That(GoldenDomains.Lowerable.All(d => d.Compiles && d.SkipReason is null)).IsTrue();
        await Assert.That(GoldenDomains.Skipped.All(d => !d.Compiles && !string.IsNullOrWhiteSpace(d.SkipReason)))
            .IsTrue().Because("every skipped domain is recorded with a reason");

        // The probe-only set is pinned by name: swapping one path for another fails here.
        var compilable = GoldenDomains.Compilable.Select(d => d.Path).ToHashSet(StringComparer.Ordinal);
        var probeOnly = GoldenDomains.Lowerable.Select(d => d.Path)
            .Where(p => !compilable.Contains(p))
            .Order(StringComparer.Ordinal);
        await Assert.That(string.Join("\n", probeOnly)).IsEqualTo(string.Join("\n", ProbeOnlySet));

        // Pinned counts: a probe leaving the corpus fails here, not silently.
        await Assert.That(GoldenDomains.Lowerable.Count).IsEqualTo(23);
        await Assert.That(GoldenDomains.Skipped.Count).IsEqualTo(2);
    }
}