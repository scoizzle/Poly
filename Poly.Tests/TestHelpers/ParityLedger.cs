namespace Poly.Tests.TestHelpers;

/// <summary>
/// One runnable parity seed: the two DSL texts (the same text unless the row breaks one side on
/// purpose), the steps, and the exception types it records as an outcome instead of a harness fault.
/// Ledger seeds read post-throw state by default, because a kept cascade is only visible afterwards;
/// the legacy <c>FromDsl</c>/<c>FromDsls</c> rows keep it off so no existing row flips.
/// </summary>
public sealed record ParityCase(
    string Name, int Issue, string SimulateDsl, string PrintedDsl, Action<ParitySide> Steps,
    string[] RecordedExceptions, bool PostThrowState = true) {
    /// <summary>Unique per case, not per issue: two seeds may carry one issue.</summary>
    public string AssemblyName =>
        "ParityLedger" + Issue + string.Concat(Name.Split('-').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}

/// <summary>
/// One known simulate/printed divergence: the scenario that reproduces it, the sides that ran, and
/// the exact set of <c>(index, step, field)</c> differences it produces, values included. The slice
/// named by <see cref="Theme"/> deletes this entry when it fixes the divergence; a stale entry is a
/// failure, never a narrowing.
/// </summary>
public sealed record KnownDomainDivergence(
    int Issue, string Theme, string Scenario, string[] Sides, ParityDifference[] ExpectedDifference);

/// <summary>
/// A seed that does not diverge at the module level, the slice that owns the fix where it does
/// (<see cref="Owner"/>, a slice id), and why. An exclusion is a recorded decision, never a skipped
/// test.
/// </summary>
public sealed record DomainParityExclusion(int Issue, string Owner, string Reason);

/// <summary>
/// What one seeded case measured: the sides that ran, their exact difference set, the outcomes, and
/// the fault when the seed itself threw. A fault belongs to its own seed and fails only that seed.
/// </summary>
public sealed record ParityRun(
    IReadOnlyList<string> Sides, IReadOnlyList<ParityDifference> Differences,
    IReadOnlyList<ParityOutcome> Simulate, IReadOnlyList<ParityOutcome> Printed, ParityScenario Scenario,
    string? Fault = null) {
    public string Describe() => Differences.Count == 0
        ? "no difference"
        : string.Join("; ", Differences.Select(d => $"[{d.Index}] {d.Step} {d.Field} ('{d.Simulate}' vs '{d.Printed}')"));
}

/// <summary>Every known divergence, sorted by issue. Empty entries are deleted by the fixing slice.</summary>
public static class KnownDomainDivergences {
    static readonly string[] Fields = ["success", "message", "failureKind", "exceptionType", "stepCount"];

    /// <summary>The ledger vocabulary: a field outside it is a typo, and a typo cannot pass.</summary>
    public static bool IsKnownField(string field) =>
        Fields.Contains(field, StringComparer.Ordinal)
        || (field.StartsWith("state:", StringComparison.Ordinal) && field.Length > "state:".Length);

    public static IReadOnlyList<KnownDomainDivergence> All { get; } = [
        new(493, "L-11", "unique-number", ["simulate", "printed"], [
            new(0, "create Thing", "success", "False", "True"),
            new(0, "create Thing", "message", "Object reference not set to an instance of an object.", null),
            new(0, "create Thing", "exceptionType", "NullReferenceException", null),
            new(0, "create Thing", "state:K", "(absent)", "9"),
            // A StateOf artifact, not a second bug: a failed create has no state of its own, and
            // StateOf reports a Stage key for a model with no stages. Known L-11 fixes the create.
            new(0, "create Thing", "state:Stage", "(absent)", null),
            new(1, "create Thing", "message", "Object reference not set to an instance of an object.",
                "Unique constraint violated: 'K' value is already used on another 'Thing'."),
            new(1, "create Thing", "exceptionType", "NullReferenceException", null),
        ]),
        new(565, "L-03", "multi-assign", ["simulate", "printed"], [
            new(1, "invoke Settle", "state:Status", "Active", "Suspended"),
        ]),
        new(599, "L-05", "cascade-overflow", ["simulate", "printed"], [
            new(6, "invoke Poke", "state:Hops", "0", "16"),
            new(6, "invoke Poke", "state:Stage", "X", "Y"),
            new(7, "use Left", "state:Hops", "0", "16"),
        ]),
        // Same family as cascade-overflow: a guard throws after the cascade has run, the module
        // rolls back to the stage it started in, and the printed side keeps the stage it reached.
        new(599, "L-05", "guarded-cycle-loop-guard", ["simulate", "printed"], [
            new(1, "invoke Go", "state:Stage", "A", "C"),
        ]),
        new(605, "T-09", "match-decimal-else", ["simulate", "printed"], [
            new(1, "invoke Price", "state:Dec", null, "2"),
        ]),
        new(607, "L-25", "create-in-iterated-collection", ["simulate", "printed"], [
            new(4, "invoke Grow", "state:lines", "1", "2"),
        ]),
    ];
}

/// <summary>Seeds that do not diverge through the module, with the owner that has to fix them.</summary>
public static class DomainParityExclusions {
    public static IReadOnlyList<DomainParityExclusion> All { get; } = [
        new(611, "L-11", "Both sides throw NullReferenceException on the unset optional unique Text, so the "
            + "module has no divergence to ratchet."),
        new(617, "L-05", "The sides agree; absolute atomicity is L-05's done-when."),
        new(621, "L-08", "The printed Create fills an omitted required parameter with its default, so a "
            + "module-level omission cannot be represented in this harness; Date/DateTime/Duration need "
            + "`uses temporal`, which this harness cannot parse on the simulate side."),
        new(622, "R-04", "Both sides report the module failure text `blocked by policy`, so the guard wording "
            + "no longer differs."),
    ];
}

/// <summary>
/// The seeded corpus. Each case is built once and shared (one Roslyn compilation each, and the
/// suite runs four tests at a time), so every test that needs a measurement reads the same run.
/// </summary>
public static class DomainParityScenarios {
    public static IReadOnlyList<ParityCase> All { get; } = [
        new("multi-assign", 565, """
            domain Library
            MemberStatus: enum { Active, Suspended }

            Member: entity {
              Status: MemberStatus default(Active)
              FineBalance: Number range(0, 100000) default(0)
              Settle: action (amount: Number) {
                assign Status to Suspended
                assign FineBalance to (FineBalance - amount)
              }
            }
            """, """
            domain Library
            MemberStatus: enum { Active, Suspended }

            Member: entity {
              Status: MemberStatus default(Active)
              FineBalance: Number range(0, 100000) default(0)
              Settle: action (amount: Number) {
                assign Status to Suspended
                assign FineBalance to (FineBalance - amount)
              }
            }
            """,
            side => {
                side.Create("Member");
                side.Invoke("Settle", ("amount", 10L));
            }, []),

        new("cascade-overflow", 599, """
            domain Cycle

            Left: entity {
              Name: Text
              Hops: Number default(0)
              rights: many Right
              A: stage { when rights X, Y { assign Hops to Hops + 1 transition to B } }
              B: stage { when rights X, Y { assign Hops to Hops + 1 transition to A } }
            }

            Right: entity {
              Name: Text
              Hops: Number default(0)
              lefts: many Left
              X: stage {
                Poke: action { transition to Y }
                when lefts A, B { assign Hops to Hops + 1 transition to Y }
              }
              Y: stage { when lefts A, B { assign Hops to Hops + 1 transition to X } }
            }
            """, """
            domain Cycle

            Left: entity {
              Name: Text
              Hops: Number default(0)
              rights: many Right
              A: stage { when rights X, Y { assign Hops to Hops + 1 transition to B } }
              B: stage { when rights X, Y { assign Hops to Hops + 1 transition to A } }
            }

            Right: entity {
              Name: Text
              Hops: Number default(0)
              lefts: many Left
              X: stage {
                Poke: action { transition to Y }
                when lefts A, B { assign Hops to Hops + 1 transition to Y }
              }
              Y: stage { when lefts A, B { assign Hops to Hops + 1 transition to X } }
            }
            """,
            side => {
                var left = side.Create("Left", ("Name", "L"));
                var right = side.Create("Right", ("Name", "R"));
                side.Use(left);
                side.Link("rights", right);
                side.Use(right);
                side.Link("lefts", left);
                side.Invoke("Poke");
                side.Use(left);
            }, []),

        new("guarded-cycle-loop-guard", 599, """
            domain E6
            Z: entity {
              Ready: Boolean
              A: stage { Go: action { transition to B } }
              B: stage { entry { if (Ready is true) { transition to C } } }
              C: stage { entry { if (Ready is true) { transition to B } } }
            }
            """, """
            domain E6
            Z: entity {
              Ready: Boolean
              A: stage { Go: action { transition to B } }
              B: stage { entry { if (Ready is true) { transition to C } } }
              C: stage { entry { if (Ready is true) { transition to B } } }
            }
            """,
            side => {
                side.Create("Z", ("Ready", true));
                side.Invoke("Go");
            }, []),

        new("handler-range-after-transition", 617, """
            domain M

            Dept: entity {
              Count: Number range(0, 10) default(0)
              emps: many Emp
              when emps Gone { assign Count to Count - 1 }
            }

            Emp: entity {
              Name: Text length(1, 20)
              Live: stage {
                Leave: action { transition to Gone }
              }
              Gone: stage { }
            }
            """, """
            domain M

            Dept: entity {
              Count: Number range(0, 10) default(0)
              emps: many Emp
              when emps Gone { assign Count to Count - 1 }
            }

            Emp: entity {
              Name: Text length(1, 20)
              Live: stage {
                Leave: action { transition to Gone }
              }
              Gone: stage { }
            }
            """,
            side => {
                var dept = side.Create("Dept");
                var emp = side.Create("Emp", ("Name", "a"));
                side.Use(dept);
                side.Link("emps", emp);
                side.Use(emp);
                side.Invoke("Leave");
                side.Invoke("Leave");
            }, []),

        new("match-decimal-else", 605, """
            domain P

            Inv: entity {
              Qty: Number default(0)
              Dec: Decimal default(0)
              A: stage {
                Price: action { assign Dec to match { Qty > 1 -> 1.5, else -> 2.0 } }
              }
            }
            """, """
            domain P

            Inv: entity {
              Qty: Number default(0)
              Dec: Decimal default(0)
              A: stage {
                Price: action { assign Dec to match { Qty > 1 -> 1.5, else -> 2.0 } }
              }
            }
            """,
            side => {
                side.Create("Inv");
                side.Invoke("Price");
            }, []),

        new("omitted-required-value-types", 621, """
            domain Mini

            Patient: entity {
              Name: Text
            }

            Appt: entity {
              Fee: Decimal
            }
            """, """
            domain Mini

            Patient: entity {
              Name: Text
            }

            Appt: entity {
              Fee: Decimal
            }
            """,
            side => {
                side.Create("Patient", ("Name", "n"));
                side.Create("Appt");
            }, []),

        new("policy-guard-message", 622, """
            domain Mini

            Cart: entity {
              Count: Number default(0)
              NotEmpty: policy { Count > 0 }
              Checkout: action require NotEmpty {
                assign Count to Count
              }
            }
            """, """
            domain Mini

            Cart: entity {
              Count: Number default(0)
              NotEmpty: policy { Count > 0 }
              Checkout: action require NotEmpty {
                assign Count to Count
              }
            }
            """,
            side => {
                side.Create("Cart");
                side.Invoke("Checkout");
            }, []),

        new("optional-unique-text", 611, """
            domain M

            Thing: entity {
              Name: Text length(2, 10)
              Tag: Text unique optional
            }
            """, """
            domain M

            Thing: entity {
              Name: Text length(2, 10)
              Tag: Text unique optional
            }
            """,
            side => {
                side.Create("Thing", ("Name", "aa"));
                side.Create("Thing", ("Name", "cc"), ("Tag", "t"));
                side.Create("Thing", ("Name", "dd"), ("Tag", "t"));
            }, ["NullReferenceException"]),

        new("unique-number", 493, """
            domain M

            Thing: entity {
              K: Number unique
            }
            """, """
            domain M

            Thing: entity {
              K: Number unique
            }
            """,
            side => {
                side.Create("Thing", ("K", 9L));
                side.Create("Thing", ("K", 9L));
            }, ["NullReferenceException"]),

        new("create-in-iterated-collection", 607, """
            domain Edge

            Inv: entity {
              Qty: Number default(0)
              lines: many Ln
              Open: stage {
                Grow: action { for l in lines { create in lines { Qty: 1 } } }
              }
            }

            Ln: entity { Qty: Number default(0) }
            """, """
            domain Edge

            Inv: entity {
              Qty: Number default(0)
              lines: many Ln
              Open: stage {
                Grow: action { for l in lines { create in lines { Qty: 1 } } }
              }
            }

            Ln: entity { Qty: Number default(0) }
            """,
            side => {
                var inv = side.Create("Inv");
                var line = side.Create("Ln");
                side.Use(inv);
                side.Link("lines", line);
                side.Invoke("Grow");
            }, []),
    ];

    static readonly Lazy<IReadOnlyDictionary<string, ParityRun>> Runs = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    public static ParityRun Run(string name) =>
        Runs.Value.TryGetValue(name, out var run)
            ? run
            : throw new ArgumentException($"No seeded parity case named '{name}'.");

    /// <summary>
    /// Builds every seed once and keeps a fault against the seed that raised it, so a broken seed
    /// fails its own assertions instead of every test that reads a measurement.
    /// </summary>
    static IReadOnlyDictionary<string, ParityRun> Build() {
        var runs = new Dictionary<string, ParityRun>(StringComparer.Ordinal);
        foreach (var seed in All) {
            try {
                runs[seed.Name] = Execute(seed);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) {
                runs[seed.Name] = new([], [], [], [], null!, $"{ex.GetType().Name}: {ex.Message}");
            }
        }
        return runs;
    }

    static ParityRun Execute(ParityCase seed) {
        var scenario = ParityScenario.FromDsls(
            seed.SimulateDsl, seed.PrintedDsl, seed.AssemblyName, seed.RecordedExceptions, seed.PostThrowState);
        var (simulate, printed) = scenario.Run(seed.Steps);
        return new(scenario.Sides, ParityScenario.DifferenceSet(simulate, printed), simulate, printed, scenario);
    }
}

/// <summary>How a ledger entry and the live difference set disagree.</summary>
public enum ParityDriftKind { Widened, Stale, ValueChanged, Renamed, Deleted }

/// <summary>One way the live set and the ledger entry disagree.</summary>
public sealed record ParityDrift(ParityDriftKind Kind, string Detail);

/// <summary>
/// Exact-set matching over <see cref="ParityDifference"/> records, keyed on
/// <c>(index, field)</c> and compared with values and the step name. Empty means the ledger entry
/// is exactly what the sides do.
/// </summary>
public static class ParityDifferenceSet {
    public static IReadOnlyList<ParityDrift> Compare(
        IReadOnlyList<ParityDifference> expected, IReadOnlyList<ParityDifference> live) {
        var drifts = new List<ParityDrift>();
        var liveByField = live.GroupBy(d => (d.Index, d.Field)).ToDictionary(group => group.Key, group => group.First());
        var liveIndexes = live.Select(d => d.Index).ToHashSet();
        var expectedFields = expected.Select(d => (d.Index, d.Field)).ToHashSet();

        foreach (var want in expected) {
            if (!liveByField.TryGetValue((want.Index, want.Field), out var got)) {
                drifts.Add(liveIndexes.Contains(want.Index)
                    ? new(ParityDriftKind.Stale, $"[{want.Index}] {want.Step} {want.Field}: missing from the live set")
                    : new(ParityDriftKind.Deleted, $"[{want.Index}] {want.Step} {want.Field}: the step no longer differs, or no longer runs"));
                continue;
            }
            if (!string.Equals(want.Step, got.Step, StringComparison.Ordinal)) {
                drifts.Add(new(ParityDriftKind.Renamed, $"[{want.Index}] {want.Field}: step '{want.Step}' is now '{got.Step}'"));
                continue;
            }
            if (!string.Equals(want.Simulate, got.Simulate, StringComparison.Ordinal)
                || !string.Equals(want.Printed, got.Printed, StringComparison.Ordinal))
                drifts.Add(new(ParityDriftKind.ValueChanged,
                    $"[{want.Index}] {want.Step} {want.Field}: ('{want.Simulate}' vs '{want.Printed}') is now ('{got.Simulate}' vs '{got.Printed}')"));
        }

        foreach (var got in live)
            if (!expectedFields.Contains((got.Index, got.Field)))
                drifts.Add(new(ParityDriftKind.Widened,
                    $"[{got.Index}] {got.Step} {got.Field}: ('{got.Simulate}' vs '{got.Printed}') is unlisted"));

        return drifts;
    }
}