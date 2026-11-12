using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// The ratchet over known simulate/printed divergences. A scenario is seeded once, measured, and
/// pinned to an exact set of <c>(index, step, field)</c> keys; the fixing slice deletes its entry.
/// A flip is a finding, never a row edit.
/// </summary>
public class DomainParityLedgerTests {
    const string ThemeFormat = @"\A[A-Z]{1,2}-[0-9]{2}\z";

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
            if (!System.Text.RegularExpressions.Regex.IsMatch(entry.Theme, ThemeFormat))
                problems.Add($"#{entry.Issue}: theme '{entry.Theme}' is not a slice id ({ThemeFormat}).");
            if (entry.ExpectedDifference.Length == 0)
                problems.Add($"#{entry.Issue}: an entry with no expected difference cannot ratchet.");
            foreach (var difference in entry.ExpectedDifference)
                if (!KnownDomainDivergences.IsKnownField(difference.Field))
                    problems.Add($"#{entry.Issue}: '{difference.Field}' is outside the field vocabulary.");
        }

        var duplicates = KnownDomainDivergences.All.GroupBy(entry => (entry.Issue, entry.Scenario))
            .Where(group => group.Count() > 1)
            .Select(group => $"#{group.Key.Issue} '{group.Key.Scenario}' is listed {group.Count()} times.");
        problems.AddRange(duplicates);

        var issues = KnownDomainDivergences.All.Select(entry => entry.Issue).ToList();
        if (!issues.SequenceEqual(issues.Order()))
            problems.Add($"entries are not sorted by issue: {string.Join(",", issues)}.");

        await Assert.That(string.Join("\n", problems)).IsEmpty();
    }

    [Test]
    public async Task Ledger_EveryExclusion_NamesItsOwnerAndNoLiveEntry() {
        var seeds = DomainParityScenarios.All.ToDictionary(scenario => scenario.Name, scenario => scenario.Issue);
        var problems = new List<string>();

        foreach (var exclusion in DomainParityExclusions.All) {
            if (exclusion.Reason.Length < 20)
                problems.Add($"#{exclusion.Issue}: reason '{exclusion.Reason}' is under 20 characters.");
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
            var entry = KnownDomainDivergences.All.FirstOrDefault(e => e.Scenario == seed.Name);
            var excluded = DomainParityExclusions.All.Any(e => e.Issue == seed.Issue);

            if (run.Differences.Count == 0) {
                if (entry is not null)
                    problems.Add($"#{seed.Issue} '{seed.Name}': listed in the ledger, but the sides agree.");
                if (!excluded)
                    problems.Add($"#{seed.Issue} '{seed.Name}': no live divergence and no exclusion.");
                continue;
            }

            if (excluded)
                problems.Add($"#{seed.Issue} '{seed.Name}': excluded, but it still diverges: {run.Describe()}.");
            if (entry is null) {
                problems.Add($"#{seed.Issue} '{seed.Name}': unlisted divergence: {run.Describe()}.");
                continue;
            }
            foreach (var drift in ParityDifferenceSet.Compare(entry.ExpectedDifference, run.Differences))
                problems.Add($"#{seed.Issue} '{seed.Name}': {drift.Kind}: {drift.Detail}");
        }

        await Assert.That(string.Join("\n", problems)).IsEmpty().Because("\n" + Table());
    }

    [Test]
    public async Task EveryLedgerEntry_StillDiverges() {
        await Assert.That(KnownDomainDivergences.All.Count).IsGreaterThan(0);

        var problems = new List<string>();
        foreach (var entry in KnownDomainDivergences.All) {
            var run = DomainParityScenarios.Run(entry.Scenario);
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

    /// <summary>The column ships compared and empty: R-03/R-04 populate it, so no entry carries a kind today.</summary>
    [Test]
    public async Task FailureKind_IsComparedButNullUntilR03() {
        var simulate = new ParityOutcome("invoke Close", false, null, null, new Dictionary<string, string?>(), FailureKind: "Invariant");
        var printed = simulate with { FailureKind = null };
        var differences = ParityScenario.DifferenceSet([simulate], [printed]);
        await Assert.That(differences.Count).IsEqualTo(1);
        await Assert.That(differences[0].Field).IsEqualTo("failureKind");
        await Assert.That(differences[0].ToString()).Contains("failure kind differs");

        var run = DomainParityScenarios.Run(DomainParityScenarios.All[0].Name);
        await Assert.That(run.Simulate.Concat(run.Printed).All(outcome => outcome.FailureKind is null)).IsTrue();
    }

    // A6: the matcher is the ratchet's only guard against a silent drift, so each drift kind gets a
    // synthetic case. A comparer that always returns empty fails all five.
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
}