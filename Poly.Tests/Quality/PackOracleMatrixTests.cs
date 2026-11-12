using System.Text.RegularExpressions;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Quality;

/// <summary>
/// The pack oracle request matrix (theme #691, slice Q-07): every <see cref="GoldenDomains.Compilable"/> domain is
/// derived into an app (<c>uses http</c> and <c>uses sqlite</c>), booted once by <see cref="PackOracle"/> and driven
/// through the <see cref="RequestMatrix"/>. The failing probes must equal <see cref="KnownPackDivergences"/>: a probe
/// that fails and is not listed, or a row whose probe passes, fails the run. The fixing slice deletes its rows.
/// </summary>
[NotInParallel("PackOracleMatrix")]
public class PackOracleMatrixTests {
    public static IEnumerable<Func<GoldenDomain>> SubsetDomains() =>
        GoldenPackDomains.Subset.Select<GoldenDomain, Func<GoldenDomain>>(d => () => d);

    public static IEnumerable<Func<GoldenDomain>> FullDomains() =>
        GoldenPackDomains.Full.Select<GoldenDomain, Func<GoldenDomain>>(d => () => d);

    private static readonly Regex ObservedShape = new(@"\Astatus=[0-9]{1,3};location=(yes|none);json=(array|object|none|other)\z");

    [After(Class)]
    public static async Task NoRecordedChildSurvivesTheClass() {
        var alive = PackOracle.RecordedChildPids.Where(PackOracle.IsProcessAlive).ToArray();
        await Assert.That(alive).IsEmpty();
    }

    // ───────────────────────────── pure: derivation, tables, vocabulary ─────────────────────────────

    private static GoldenDomain Golden(string name) => GoldenDomains.Compilable.Single(d => d.Name == name);

    private static string[] Uses(string poly) =>
        [.. poly.ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("uses ", StringComparison.Ordinal))];

    [Test]
    public async Task FromGolden_Derived_InjectsHttpAndSqliteOnce_KeepsRest() {
        var hotel = GoldenPackDomains.FromGolden(Golden("hotel"));

        await Assert.That(Uses(hotel.Poly).Count(u => u == "uses http")).IsEqualTo(1);
        await Assert.That(Uses(hotel.Poly).Count(u => u == "uses sqlite")).IsEqualTo(1);
        await Assert.That(Uses(hotel.Poly)).Contains("uses temporal");
        await Assert.That(hotel.Poly).Contains("RoomType: entity");
        await Assert.That(hotel.Name).IsEqualTo("golden-hotel");
    }

    [Test]
    public async Task FromGolden_ReplacesPersistence_NeverLoadsBothStoragePasses() {
        foreach (var golden in GoldenDomains.Compilable) {
            var uses = Uses(GoldenPackDomains.FromGolden(golden).Poly);
            await Assert.That(uses.Count(u => u == "uses http")).IsEqualTo(1);
            await Assert.That(uses.Count(u => u == "uses sqlite")).IsEqualTo(1);
            await Assert.That(uses.Contains("uses persistence") && uses.Contains("uses sqlite")).IsFalse();
            await Assert.That(uses[^1]).IsEqualTo("uses sqlite");
        }

        // hotel: temporal + persistence; mcp-library: no uses at all; simulate-create-in: temporal + storage.
        await Assert.That(Uses(GoldenPackDomains.FromGolden(Golden("hotel")).Poly))
            .IsEquivalentTo(new[] { "uses temporal", "uses http", "uses sqlite" });
        await Assert.That(Uses(GoldenPackDomains.FromGolden(Golden("mcp-library")).Poly))
            .IsEquivalentTo(new[] { "uses http", "uses sqlite" });
        await Assert.That(Uses(GoldenPackDomains.FromGolden(Golden("simulate-create-in")).Poly))
            .IsEquivalentTo(new[] { "uses temporal", "uses storage", "uses http", "uses sqlite" });
    }

    [Test]
    public async Task GoldenPackDomains_SubsetIsTwo_AndInFull() {
        await Assert.That(GoldenPackDomains.Subset.Select(d => d.Name)).IsEquivalentTo(new[] { "mcp-library", "warehouse" });
        await Assert.That(GoldenPackDomains.Subset.All(d => GoldenPackDomains.Full.Contains(d))).IsTrue();
        await Assert.That(GoldenPackDomains.Full.Count + GoldenPackDomains.NoBoot.Count).IsEqualTo(GoldenDomains.Compilable.Count);
    }

    [Test]
    public async Task RequestMatrix_CoversEveryCompilableDomain_OrNamesNoBoot() {
        var covered = RequestMatrix.All.Select(m => m.Domain).ToList();
        var noBoot = GoldenPackDomains.NoBoot.Select(n => n.Domain).ToList();
        var problems = new List<string>();

        foreach (var golden in GoldenDomains.Compilable) {
            var inMatrix = covered.Contains(golden.Name);
            var named = noBoot.Contains(golden.Name);
            if (!inMatrix && !named)
                problems.Add($"'{golden.Name}': neither a matrix row nor a NoBoot entry.");
            if (inMatrix && named)
                problems.Add($"'{golden.Name}': in the matrix and NoBoot.");
        }
        foreach (var domain in covered.Concat(noBoot).Where(n => GoldenDomains.Compilable.All(g => g.Name != n)))
            problems.Add($"'{domain}': not a Compilable golden domain.");
        foreach (var n in GoldenPackDomains.NoBoot) {
            if (n.Issue <= 0 || !KnownPackDivergences.IsSliceId(n.Owner) || n.Reason.Length < 20
                || !n.Reason.StartsWith($"#{n.Issue}:", StringComparison.Ordinal))
                problems.Add($"NoBoot '{n.Domain}': needs Issue > 0, a slice-id Owner and a Reason of at least 20 characters starting '#{n.Issue}:'.");
        }

        await Assert.That(problems).IsEmpty();
    }

    private static Domain Parse(string poly) {
        if (!Uses(poly).Contains("uses persistence")) {
            var nl = poly.IndexOf('\n');
            poly = poly[..(nl + 1)] + "uses persistence\n" + poly[(nl + 1)..];
        }
        var changes = new PolyDslParser(poly, ExtensionCatalog.Core.Authoring).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException("Domain evolution failed: " + string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
        return result.Root!;
    }

    [Test]
    public async Task RequestMatrix_Rows_MatchTheirDomainModel() {
        var problems = new List<string>();
        foreach (var matrix in RequestMatrix.All) {
            var domain = Parse(Golden(matrix.Domain).Poly);
            foreach (var row in matrix.Entities) {
                var entity = domain.Types.OfType<Entity>().SingleOrDefault(e => e.Name == row.Entity);
                if (entity is null) {
                    problems.Add($"{matrix.Domain}: entity '{row.Entity}' does not exist.");
                    continue;
                }
                var required = entity.Properties
                    .Where(p => !p.Constraints.Any(c => c is OptionalConstraint or DefaultValueConstraint))
                    .Select(p => p.Name).Order(StringComparer.Ordinal).ToList();
                var bodyKeys = row.Valid.Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList();
                if (!required.SequenceEqual(bodyKeys))
                    problems.Add($"{matrix.Domain}.{row.Entity}: valid body [{string.Join(",", bodyKeys)}] but required [{string.Join(",", required)}].");
                if (row.KeyProperty is not null
                    && !entity.Properties.Any(p => p.Name == row.KeyProperty && p.Constraints.Any(c => c is UniqueConstraint)))
                    problems.Add($"{matrix.Domain}.{row.Entity}: '{row.KeyProperty}' is not a unique property.");
                if (row.Action is { } probe) {
                    var action = entity.Actions.Concat(entity.Stages.SelectMany(s => s.Actions)).SingleOrDefault(a => a.Name == probe.Name);
                    if (action is null)
                        problems.Add($"{matrix.Domain}.{row.Entity}: action '{probe.Name}' does not exist.");
                    else {
                        var names = action.Parameters.Select(p => p.Name).Order(StringComparer.Ordinal).ToList();
                        foreach (var args in new[] { probe.Valid, probe.Rejected }.Where(a => a is not null)) {
                            var given = args!.Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList();
                            if (!names.SequenceEqual(given))
                                problems.Add($"{matrix.Domain}.{row.Entity}.{probe.Name}: arguments [{string.Join(",", given)}] but parameters [{string.Join(",", names)}].");
                        }
                        if (action.Parameters.Count == 0 && probe.Rejected is not null)
                            problems.Add($"{matrix.Domain}.{row.Entity}.{probe.Name}: a parameterless action has nothing to reject.");
                    }
                }
            }
        }

        await Assert.That(problems).IsEmpty();
    }

    [Test]
    public async Task KnownPackDivergences_AreSorted_HaveIssueOwnerAndKnownProbe() {
        var problems = new List<string>();
        var all = KnownPackDivergences.All;
        var sorted = all.OrderBy(r => r.Issue).ThenBy(r => r.Provider, StringComparer.Ordinal)
            .ThenBy(r => r.Domain, StringComparer.Ordinal).ThenBy(r => r.Probe, StringComparer.Ordinal).ToList();
        if (!all.SequenceEqual(sorted))
            problems.Add("the ledger is not sorted by (Issue, Provider, Domain, Probe).");
        foreach (var dup in all.GroupBy(r => (r.Provider, r.Domain, r.Probe)).Where(g => g.Count() > 1))
            problems.Add($"duplicate row {dup.Key}.");

        foreach (var row in all) {
            if (row.Issue <= 0)
                problems.Add($"{row.Domain} {row.Probe}: Issue must be a real issue number.");
            if (!KnownPackDivergences.IsSliceId(row.Owner))
                problems.Add($"#{row.Issue}: Owner '{row.Owner}' is not a slice id.");
            if (row.Provider != GoldenPackDomains.Provider)
                problems.Add($"#{row.Issue}: Provider '{row.Provider}' is not 'sqlite'.");
            var matrix = RequestMatrix.All.SingleOrDefault(m => m.Domain == row.Domain);
            if (matrix is null || !RequestMatrix.ProbeNames(matrix).Contains(row.Probe))
                problems.Add($"#{row.Issue}: '{row.Domain}' has no probe '{row.Probe}'.");
            if (!ObservedShape.IsMatch(row.Observed))
                problems.Add($"#{row.Issue} {row.Domain} {row.Probe}: Observed '{row.Observed}' is not the coarse shape.");
        }

        await Assert.That(problems).IsEmpty();
    }

    [Test]
    public async Task IsSliceId_MatchesQ06Shape() {
        await Assert.That(KnownPackDivergences.IsSliceId("P-12")).IsTrue();
        await Assert.That(KnownPackDivergences.IsSliceId("QQ-07")).IsTrue();
        await Assert.That(KnownPackDivergences.IsSliceId("p-12")).IsFalse();
        await Assert.That(KnownPackDivergences.IsSliceId("P-1")).IsFalse();
        await Assert.That(KnownPackDivergences.IsSliceId("PPP-12")).IsFalse();
        await Assert.That(KnownPackDivergences.IsSliceId("P-12\n")).IsFalse();
    }

    [Test]
    public async Task Compare_ClassifiesStaleWidenedValueChangedAndMissingProbe() {
        var row = new KnownPackDivergence(1, "P-01", "sqlite", "d", "E.CreateValid", "status=500;location=none;json=none");
        string[] names = ["E.List", "E.CreateValid"];
        ProbeObservation Ok(string p) => new(p, true, "status=200;location=none;json=array");
        ProbeObservation Bad(string p, string v) => new(p, false, v);

        var stale = PackDivergenceSet.Compare([row], new("sqlite", "d", null, [Ok("E.List"), Ok("E.CreateValid")]), names);
        var widened = PackDivergenceSet.Compare([], new("sqlite", "d", null, [Bad("E.List", "status=500;location=none;json=none"), Ok("E.CreateValid")]), names);
        var changed = PackDivergenceSet.Compare([row], new("sqlite", "d", null, [Ok("E.List"), Bad("E.CreateValid", "status=200;location=none;json=none")]), names);
        var missing = PackDivergenceSet.Compare([row], new("sqlite", "d", null, [Ok("E.List")]), names);
        var exact = PackDivergenceSet.Compare([row], new("sqlite", "d", null, [Ok("E.List"), Bad("E.CreateValid", row.Observed)]), names);
        var fault = PackDivergenceSet.Compare([], new("sqlite", "d", "boot: x", []), names);

        await Assert.That(stale.Single().Kind).IsEqualTo(ParityDriftKind.Stale);
        await Assert.That(widened.Single().Kind).IsEqualTo(ParityDriftKind.Widened);
        await Assert.That(changed.Single().Kind).IsEqualTo(ParityDriftKind.ValueChanged);
        await Assert.That(missing.Single().Kind).IsEqualTo(ParityDriftKind.Deleted);
        await Assert.That(exact).IsEmpty();
        await Assert.That(fault.Single().Kind).IsEqualTo(ParityDriftKind.Widened);
    }

    // ───────────────────────────── oracle: boot, observe, compare ─────────────────────────────

    private static async Task AssertBaselineMatchesLedger(GoldenDomain golden) {
        var live = await MatrixObservations.ObserveAsync(GoldenPackDomains.Provider, golden);
        var expected = KnownPackDivergences.All.Where(r => r.Domain == golden.Name && r.Provider == GoldenPackDomains.Provider).ToList();
        var drifts = PackDivergenceSet.Compare(expected, live, RequestMatrix.ProbeNames(RequestMatrix.For(golden.Name)));
        await Assert.That(drifts.Select(d => $"{d.Kind}: {d.Detail}")).IsEmpty();
    }

    [Test]
    [Category("OracleSubset")]
    [Timeout(300_000)]
    [MethodDataSource(nameof(SubsetDomains))]
    public async Task Boot_SubsetDomain_BaselineMatrix_MatchesLedger(GoldenDomain golden, CancellationToken cancellationToken) {
        _ = cancellationToken;
        await AssertBaselineMatchesLedger(golden);
    }

    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    [MethodDataSource(nameof(FullDomains))]
    public async Task Boot_FullSetDomain_BaselineMatrix_MatchesLedger(GoldenDomain golden, CancellationToken cancellationToken) {
        _ = cancellationToken;
        await AssertBaselineMatchesLedger(golden);
    }

    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task Ledger_EveryEntry_StillDiverges(CancellationToken cancellationToken) {
        _ = cancellationToken;
        var problems = new List<string>();
        foreach (var domain in KnownPackDivergences.All.Select(r => (r.Provider, r.Domain)).Distinct()) {
            var golden = Golden(domain.Domain);
            var live = await MatrixObservations.ObserveAsync(domain.Provider, golden);
            var rows = KnownPackDivergences.All.Where(r => r.Provider == domain.Provider && r.Domain == domain.Domain).ToList();
            problems.AddRange(PackDivergenceSet.Compare(rows, live, RequestMatrix.ProbeNames(RequestMatrix.For(domain.Domain)))
                .Where(d => d.Kind is ParityDriftKind.Stale or ParityDriftKind.ValueChanged or ParityDriftKind.Deleted)
                .Select(d => $"{d.Kind}: {d.Detail}"));
        }
        await Assert.That(problems).IsEmpty();
    }

    /// <summary>Ledger-conditional: a row means the observation equals its <c>Observed</c>; no row means the real
    /// contract holds. A fixing slice deletes the row and nothing else.</summary>
    private static async Task AssertProbeMatchesLedgerOrContract(string domainName, string probe, string contract) {
        var live = await MatrixObservations.ObserveAsync(GoldenPackDomains.Provider, Golden(domainName));
        await Assert.That(live.Fault).IsNull();
        var got = live.Probes.Single(p => p.Probe == probe);
        var row = KnownPackDivergences.All.SingleOrDefault(r =>
            r.Provider == GoldenPackDomains.Provider && r.Domain == domainName && r.Probe == probe);
        if (row is null) {
            await Assert.That(got.Passed).IsTrue();
            await Assert.That(got.Observed).StartsWith(contract);
        }
        else {
            await Assert.That(got.Passed).IsFalse();
            await Assert.That(got.Observed).IsEqualTo(row.Observed);
        }
    }

    [Test]
    [Category("OracleSubset")]
    [Timeout(300_000)]
    public async Task Library_PostThenGetById_RoundTripsOnSqlite(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await AssertProbeMatchesLedgerOrContract("mcp-library", "Patron.CreateValid", "status=201;location=yes");
        await AssertProbeMatchesLedgerOrContract("mcp-library", "Patron.ReadCreated", "status=200");
    }

    [Test]
    [Category("OracleFull")]
    [Timeout(300_000)]
    public async Task DecimalRange_PostValidValue_Succeeds(CancellationToken cancellationToken) {
        _ = cancellationToken;
        await AssertProbeMatchesLedgerOrContract("hotel", "RoomType.CreateValid", "status=201");
    }
}