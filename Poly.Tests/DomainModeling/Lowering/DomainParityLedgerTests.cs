using Poly.DomainModeling.Compile;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// The ratchet over known simulate/printed divergences. A scenario is seeded once, measured, and
/// pinned to an exact set of <c>(index, step, field)</c> keys; the fixing slice deletes its entry.
/// A flip is a finding, never a row edit.
/// </summary>
public class DomainParityLedgerTests {
    const string ThemeFormat = @"\A[A-Z]{1,2}-[0-9]{2}[a-z]?\z";

    internal static bool IsSliceId(string value) => System.Text.RegularExpressions.Regex.IsMatch(value, ThemeFormat);

    /// <summary>One measurement line per seed, so a failure is also the table the row records.</summary>
    static string Table() =>
        string.Join("\n", DomainParityScenarios.All.Select(scenario => {
            var run = DomainParityScenarios.Run(scenario.Name);
            return $"#{scenario.Issue} {scenario.Name} [{string.Join("+", run.Sides)}]: {run.Describe()}";
        }));

    [Test]
    public async Task Ledger_EveryEntry_NamesAKnownScenarioAndSides() {
        var names = DomainParityScenarios.All.Select(scenario => scenario.Name).ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var entry in KnownDomainDivergences.All) {
            if (!names.Contains(entry.Scenario))
                problems.Add($"#{entry.Issue}: unknown scenario '{entry.Scenario}'.");
            else {
                var sides = DomainParityScenarios.Run(entry.Scenario).Sides;
                if (!entry.Sides.SequenceEqual(sides, StringComparer.Ordinal))
                    problems.Add($"#{entry.Issue} '{entry.Scenario}': sides [{string.Join(",", entry.Sides)}] but the case runs [{string.Join(",", sides)}].");
            }
            if (!IsSliceId(entry.Theme))
                problems.Add($"#{entry.Issue}: theme '{entry.Theme}' is not a slice id ({ThemeFormat}).");
            if (entry.ExpectedDifference.Length == 0)
                problems.Add($"#{entry.Issue}: an entry with no expected difference cannot ratchet.");
            foreach (var difference in entry.ExpectedDifference)
                if (!KnownDomainDivergences.IsKnownField(difference.Field))
                    problems.Add($"#{entry.Issue}: '{difference.Field}' is outside the field vocabulary.");
            // One entry may name a step field once; a duplicate would let the comparer match either.
            var repeatedKeys = entry.ExpectedDifference.GroupBy(difference => (difference.Index, difference.Field))
                .Where(group => group.Count() > 1)
                .Select(group => $"#{entry.Issue}: [{group.Key.Index}] {group.Key.Field} is listed {group.Count()} times.");
            problems.AddRange(repeatedKeys);
        }

        var duplicates = KnownDomainDivergences.All.GroupBy(entry => (entry.Issue, entry.Scenario))
            .Where(group => group.Count() > 1)
            .Select(group => $"#{group.Key.Issue} '{group.Key.Scenario}' is listed {group.Count()} times.");
        problems.AddRange(duplicates);

        var issues = KnownDomainDivergences.All.Select(entry => entry.Issue).ToList();
        if (!issues.SequenceEqual(issues.Order()))
            problems.Add($"entries are not sorted by issue: {string.Join(",", issues)}.");

        var assemblies = DomainParityScenarios.All.Select(scenario => scenario.AssemblyName).ToList();
        if (assemblies.Count != assemblies.Distinct(StringComparer.Ordinal).Count())
            problems.Add($"assembly names are not unique: {string.Join(",", assemblies)}.");

        await Assert.That(string.Join("\n", problems)).IsEmpty();
    }

    [Test]
    public async Task Ledger_EveryExclusion_NamesItsOwnerAndNoLiveEntry() {
        var seeds = DomainParityScenarios.All.ToDictionary(scenario => scenario.Name, scenario => scenario.Issue);
        var problems = new List<string>();

        foreach (var exclusion in DomainParityExclusions.All) {
            if (exclusion.Reason.Length < 20)
                problems.Add($"#{exclusion.Issue}: reason '{exclusion.Reason}' is under 20 characters.");
            if (!IsSliceId(exclusion.Owner))
                problems.Add($"#{exclusion.Issue}: owner '{exclusion.Owner}' is not a slice id ({ThemeFormat}).");
            if (!seeds.Values.Contains(exclusion.Issue))
                problems.Add($"#{exclusion.Issue}: excluded, but no seeded case carries that issue.");
            if (KnownDomainDivergences.All.Any(entry => entry.Issue == exclusion.Issue))
                problems.Add($"#{exclusion.Issue}: excluded and listed in the ledger.");
        }

        var duplicates = DomainParityExclusions.All.GroupBy(exclusion => exclusion.Issue)
            .Where(group => group.Count() > 1)
            .Select(group => $"#{group.Key} is excluded {group.Count()} times.");
        problems.AddRange(duplicates);

        await Assert.That(string.Join("\n", problems)).IsEmpty();
    }

    /// <summary>
    /// The step-1 measurement and the ratchet in one row: every live divergence is listed, every
    /// listed entry matches the live set exactly, and a seed that does not diverge is excluded.
    /// </summary>
    [Test]
    public async Task NoUnlistedDivergence_InTheSeededCorpus() {
        var problems = new List<string>();

        foreach (var seed in DomainParityScenarios.All) {
            var run = DomainParityScenarios.Run(seed.Name);
            var entries = KnownDomainDivergences.All.Where(entry => entry.Scenario == seed.Name).ToList();
            var excluded = DomainParityExclusions.All.Any(entry => entry.Issue == seed.Issue);

            if (run.Fault is not null) {
                problems.Add($"#{seed.Issue} '{seed.Name}': the seed threw: {run.Fault}");
                continue;
            }
            if (entries.Count > 1)
                problems.Add($"#{seed.Issue} '{seed.Name}': {entries.Count} ledger entries name one scenario.");

            if (run.Differences.Count == 0) {
                if (entries.Count > 0)
                    problems.Add($"#{seed.Issue} '{seed.Name}': listed in the ledger, but the sides agree.");
                if (!excluded)
                    problems.Add($"#{seed.Issue} '{seed.Name}': no live divergence and no exclusion.");
                continue;
            }

            if (excluded)
                problems.Add($"#{seed.Issue} '{seed.Name}': excluded, but it still diverges: {run.Describe()}.");
            if (entries.Count == 0) {
                problems.Add($"#{seed.Issue} '{seed.Name}': unlisted divergence: {run.Describe()}.");
                continue;
            }
            foreach (var drift in ParityDifferenceSet.Compare(entries[0].ExpectedDifference, run.Differences))
                problems.Add($"#{seed.Issue} '{seed.Name}': {drift.Kind}: {drift.Detail}");
        }

        await Assert.That(string.Join("\n", problems)).IsEmpty().Because("\n" + Table());
    }

    /// <summary>
    /// An empty ledger is the goal state, so this row has no non-empty precondition: the comparer
    /// cases below are what guard the mechanism, and <see cref="NoUnlistedDivergence_InTheSeededCorpus"/>
    /// is what fails when a divergence is unlisted.
    /// </summary>
    [Test]
    public async Task EveryLedgerEntry_StillDiverges() {
        var problems = new List<string>();
        foreach (var entry in KnownDomainDivergences.All) {
            var run = DomainParityScenarios.Run(entry.Scenario);
            if (run.Fault is not null) {
                problems.Add($"#{entry.Issue} ({entry.Theme}) '{entry.Scenario}': the seed threw: {run.Fault}");
                continue;
            }
            foreach (var drift in ParityDifferenceSet.Compare(entry.ExpectedDifference, run.Differences))
                problems.Add($"#{entry.Issue} ({entry.Theme}) '{entry.Scenario}': {drift.Kind}: {drift.Detail}");
        }

        await Assert.That(string.Join("\n", problems)).IsEmpty().Because("\n" + Table());
    }

    /// <summary>
    /// A guard, not a divergence: the factory hands both sides the same library set from two
    /// instances, so the simulator never runs on the session that lowered the printed module.
    /// </summary>
    [Test]
    public async Task BothSides_BuildFromTheSameSessionFactory() {
        var problems = new List<string>();
        foreach (var seed in DomainParityScenarios.All) {
            var run = DomainParityScenarios.Run(seed.Name);
            if (run.Fault is not null) {
                problems.Add($"#{seed.Issue}: the seed threw: {run.Fault}");
                continue;
            }
            if (!run.Sides.SequenceEqual(["simulate", "printed"], StringComparer.Ordinal))
                problems.Add($"#{seed.Issue}: sides are [{string.Join(",", run.Sides)}].");
            var simulate = run.Scenario.SimulateSession;
            var printed = run.Scenario.PrintedSession;
            if (!simulate.Extensions.SequenceEqual(printed.Extensions, StringComparer.Ordinal))
                problems.Add($"#{seed.Issue}: simulate session [{string.Join(",", simulate.Extensions)}] vs printed [{string.Join(",", printed.Extensions)}].");
            if (ReferenceEquals(simulate, printed))
                problems.Add($"#{seed.Issue}: both sides share one session instance.");
        }

        await Assert.That(string.Join("\n", problems)).IsEmpty();
    }

    /// <summary>
    /// The factory honours the id list it is given, and always adds the storage library, so a
    /// `uses` domain and the printed side are compiled against the same set.
    /// </summary>
    [Test]
    public async Task ParitySessions_WhenGivenIds_LoadsThoseLibrariesAndStorage() {
        // "temporal" is not in the authoring seed, so the two calls must differ on it.
        var custom = ParitySessions.Create([ExtensionCatalog.TemporalId]).Extensions;
        var defaulted = ParitySessions.Create().Extensions;

        await Assert.That(custom).Contains(ExtensionCatalog.TemporalId);
        await Assert.That(custom).Contains("persistence");
        await Assert.That(defaulted).Contains(ExtensionCatalog.StorageFacetsId);
        await Assert.That(custom.SequenceEqual(defaulted)).IsFalse();
        await Assert.That(defaulted.SequenceEqual(ExtensionCatalog.ProductAuthoring.Append("persistence"))).IsTrue();
    }

    /// <summary>
    /// The typed-failure columns are populated on both sides and compared: kind, path and guard.
    /// A successful step carries none of them, and the seeded corpus carries at least one failed
    /// step with a kind, and one whose kind is not the catch-all <c>Constraint</c>.
    /// </summary>
    [Test]
    public async Task FailureKind_IsPopulatedOnBothSides() {
        var simulate = new ParityOutcome("invoke Close", false, null, null, new Dictionary<string, string?>(),
            FailureKind: "Policy", FailurePath: "Order.Close", FailureGuard: "IsOpen");
        var differences = ParityScenario.DifferenceSet([simulate], [simulate with { FailureKind = null, FailurePath = null, FailureGuard = null }]);
        await Assert.That(differences.Select(d => d.Field).ToList()).IsEquivalentTo(["failureKind", "failurePath", "failureGuard"]);
        await Assert.That(differences[0].ToString()).Contains("failure kind differs");
        await Assert.That(differences[1].ToString()).Contains("failure path differs");
        await Assert.That(differences[2].ToString()).Contains("failure guard differs");

        var runs = DomainParityScenarios.All.Select(seed => (seed, run: DomainParityScenarios.Run(seed.Name))).ToList();
        var problems = runs
            .Where(pair => pair.run.Fault is not null)
            .Select(pair => $"#{pair.seed.Issue}: the seed threw: {pair.run.Fault}")
            .ToList();
        var outcomes = new List<ParityOutcome>();
        foreach (var (seed, run) in runs.Where(pair => pair.run.Fault is null)) {
            foreach (var outcome in run.Simulate.Concat(run.Printed)) {
                outcomes.Add(outcome);
                if (outcome.Success && (outcome.FailureKind, outcome.FailurePath, outcome.FailureGuard) != (null, null, null))
                    problems.Add($"#{seed.Issue} '{seed.Name}': {outcome.Step} succeeded but carries a failure.");
            }
        }
        await Assert.That(string.Join("\n", problems)).IsEmpty();

        var typed = outcomes.Where(o => !o.Success && o.ExceptionType is null && o.FailureKind is not null).ToList();
        await Assert.That(typed).IsNotEmpty();
        await Assert.That(typed.Any(o => o.FailureKind != nameof(Poly.Runtime.DomainFailureKind.Constraint))).IsTrue();
    }

    // A6: the matcher is the ratchet's only guard against a silent drift, so each drift kind gets a
    // synthetic case. A comparer that always returns empty fails all six.
    static ParityOutcome Step(string step = "invoke Close", bool success = true, string? message = null,
        string? exceptionType = null, string? stage = "Open") =>
        new(step, success, message, exceptionType, new Dictionary<string, string?> { ["Stage"] = stage });

    static IReadOnlyList<ParityDifference> CloseStage(string value) =>
        ParityScenario.DifferenceSet([Step()], [Step(stage: value)]);

    static async Task AssertOnlyDrift(ParityDriftKind kind, IReadOnlyList<ParityDifference> expected, IReadOnlyList<ParityDifference> live) {
        var drifts = ParityDifferenceSet.Compare(expected, live);
        await Assert.That(drifts.Count).IsEqualTo(1);
        await Assert.That(drifts[0].Kind).IsEqualTo(kind);
    }

    [Test]
    public Task DifferenceSet_WhenTheLiveSetIsWider_ReportsWidened() =>
        AssertOnlyDrift(ParityDriftKind.Widened, [], CloseStage("Closed"));

    [Test]
    public Task DifferenceSet_WhenTheExpectedFieldIsGone_ReportsStale() {
        // The step still differs (state), but the entry's `success` key is gone from the live set,
        // so the entry is stale rather than stale-plus-widened.
        var expected = ParityScenario.DifferenceSet([Step(success: true)], [Step(success: false, stage: "Closed")]);
        return AssertOnlyDrift(ParityDriftKind.Stale, expected, CloseStage("Closed"));
    }

    [Test]
    public Task DifferenceSet_WhenAStepStopsRunning_ReportsDeleted() =>
        AssertOnlyDrift(ParityDriftKind.Deleted, CloseStage("Closed"), []);

    [Test]
    public Task DifferenceSet_WhenTheValueMoves_ReportsValueChanged() =>
        AssertOnlyDrift(ParityDriftKind.ValueChanged, CloseStage("Closed"), CloseStage("Other"));

    [Test]
    public Task DifferenceSet_WhenTheStepIsRenamed_ReportsRenamed() =>
        AssertOnlyDrift(ParityDriftKind.Renamed, CloseStage("Closed"),
            ParityScenario.DifferenceSet([Step("invoke Open")], [Step("invoke Open", stage: "Closed")]));

    /// <summary>
    /// A rename that also moves the value must not mask either half: the key match is on
    /// <c>(index, field)</c>, so the step name is what reports the rename, and the values are then
    /// never compared on a row whose identity already changed.
    /// </summary>
    [Test]
    public async Task DifferenceSet_WhenTheStepIsRenamedAndTheValueMoves_StillReports() {
        var drifts = ParityDifferenceSet.Compare(CloseStage("Closed"), CloseStage("Other")
            .Select(difference => difference with { Step = "invoke Open" })
            .ToList());

        await Assert.That(drifts.Count).IsEqualTo(1);
        await Assert.That(drifts[0].Kind).IsEqualTo(ParityDriftKind.Renamed);
        await Assert.That(drifts[0].Detail).Contains("invoke Close");
        await Assert.That(drifts[0].Detail).Contains("invoke Open");
    }
}