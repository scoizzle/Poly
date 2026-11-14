using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Introspection;

using Assignment = Poly.Ast.Nodes.Assignment;
using Constant = Poly.Ast.Nodes.Constant;
using DmPrimitive = Poly.DomainModeling.Ontology.PrimitiveType;
using PrimType = Poly.Introspection.PrimitiveType;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// GateParity (#679, T-05): a document analysis accepts is accepted by every door, the VM, the generated C# and
/// <c>polyc check</c>. <see cref="GateParity"/> runs them; each row here is a document and the steps it simulates.
/// </summary>
public class AnalysisGateParityTests {
    private static bool Evaluated(HarnessStep step) =>
        step.Data?["result"] is { } result
            ? result.GetValue<bool>()
            : throw new InvalidOperationException($"Evaluate step {step.Index} has no 'result': {step.Data?.ToJsonString()} ({step.Summary})");

    private static readonly SimStep[] OpenTwiceThenEvaluate = [
        SimStep.Create("Cust", alias: "c"),
        SimStep.Invoke("c", "Open"),
        SimStep.Invoke("c", "Open"),
        SimStep.Evaluate("Cust", "Big", "c"),
        SimStep.Evaluate("Cust", "Exact", "c"),
    ];

    private static string Bank(string balanceType, string exact) => $$"""
        domain T
        Cust: entity {
          accounts: many Acct
          Big: policy { sum accounts Bal > 100 }
          Exact: policy { sum accounts Bal == {{exact}} }
          Open: action { create in accounts { Bal: {{(balanceType == "Decimal" ? "1.5" : "2")}} } }
        }
        Acct: entity {
          Bal: {{balanceType}}
        }
        """;

    /// <summary>The control row: the same aggregate over a Number property, every door agrees on master.</summary>
    [Test]
    public async Task SumOverNumberProperty_AnalysesAndSimulatesAndExportsAgree() {
        var result = GateParity.Check(Bank("Number", "4"), OpenTwiceThenEvaluate);

        await Assert.That(result.Analysis).IsTrue().Because(result.ToString());
        await Assert.That(result.Holds).IsTrue().Because(result.ToString());
    }

    /// <summary>
    /// #510: <c>sum accounts Bal</c> over a Decimal property analyses clean, so every door must run it, and the
    /// sum is the exact Decimal sum (1.5 + 1.5 == 3.0), not an Int64 accumulator the VM refuses.
    /// </summary>
    [Test]
    public async Task SumOverDecimalProperty_AnalysesAndSimulatesAndExportsAgree() {
        var result = GateParity.Check(Bank("Decimal", "3.0"), OpenTwiceThenEvaluate);

        await Assert.That(result.Analysis).IsTrue().Because(result.ToString());
        await Assert.That(result.Holds).IsTrue().Because(result.ToString());

        var surface = PolySurfaceHarness.New();
        var workspace = surface.Open("Bank");
        await Assert.That(surface.Define(workspace, Bank("Decimal", "3.0")).Success).IsTrue();
        var run = surface.Simulate(workspace, OpenTwiceThenEvaluate);
        await Assert.That(Evaluated(run.Step(3))).IsFalse();
        await Assert.That(Evaluated(run.Step(4))).IsTrue();
    }

    /// <summary>#510 as filed: the issue's document and its simulate steps, through every door.</summary>
    [Test]
    public async Task Issue510_TheFiledDocument_EveryDoorAccepts() {
        var result = GateParity.Check("""
            domain T
            Cust: entity {
              accounts: many Acct
              Big: policy { sum accounts Bal > 100 }
              Open: action { create in accounts { Bal: 1.5 } }
            }
            Acct: entity {
              Bal: Decimal
            }
            """,
            SimStep.Create("Cust", alias: "c"), SimStep.Invoke("c", "Open"), SimStep.Evaluate("Cust", "Big", "c"));

        await Assert.That(result.Analysis).IsTrue().Because(result.ToString());
        await Assert.That(result.Holds).IsTrue().Because(result.ToString());
    }

    /// <summary>
    /// Seam N1 (lock #679 6841065713, amendment 10): <c>null</c> compared with a value type is accepted by analysis
    /// through T-05 (the leniency for entity, value type and Binary against <c>null</c>); T-06 owns removing that
    /// allowance. Pinned here: the VM and the generated C# both accept it too, so no door disagrees today (no
    /// CS0019: the generated value type compares with <c>null</c>). A value type cannot be a stored property
    /// (DMSTR006), so the reachable shapes are an action parameter and a function parameter.
    /// </summary>
    [Test]
    [Arguments("action parameter", """
        domain T
        Money: value {
          Amount: Number
        }
        Acct: entity {
          N: Number
          Set: action (m: Money) { if (m == null) { assign N to 1 } }
        }
        """, true)]
    [Arguments("function parameter", """
        domain T
        Money: value {
          Amount: Number
        }
        Acct: entity {
          N: Number
        }
        function IsNone(m: Money): Boolean = m == null
        """, false)]
    public async Task ValueTypeComparedToNull_N1_IsAcceptedByEveryDoor_UntilT06(string shape, string dsl, bool invoke) {
        SimStep[] steps = invoke
            ? [SimStep.Create("Acct", """{"N":0}""", "a"), SimStep.Invoke("a", "Set", """{"m":{"Amount":2}}""")]
            : [SimStep.Create("Acct", """{"N":0}""", "a")];
        var result = GateParity.Check(dsl, steps);

        await Assert.That(result.Analysis).IsTrue().Because($"{shape}: {result}");
        await Assert.That(result.Simulate is true).IsTrue().Because($"{shape}: {result}");
        await Assert.That(result.CSharp is true).IsTrue().Because($"{shape}: {result}");
        await Assert.That(result.PolycCheck is true).IsTrue().Because($"{shape}: {result}");
    }

    private const string OrderingDomain = """
        domain T
        uses temporal
        Level: enum { Low, High }
        Money: value {
          Amount: Number
        }
        Other: entity {
          X: Number
        }
        Item: entity {
          A: Number
          B: Number
          S: Text
          U: Text
          F: Boolean
          G: Boolean
          L: Level
          M: Level
          Due: Date
          Gap: Duration optional
          Dc: Decimal
          Tm: Time
          Stamp: DateTime
          Opt: Number optional
          U1: Uuid
          U2: Uuid
          peer: Other
          MEMBERS
        }
        """;

    /// <summary>
    /// T-07a (#631): ordering is defined for numbers, dates and durations, and for nothing else the provider declares
    /// no ordering for. A document that orders Text, Boolean, an enum, an entity or a value type is refused with
    /// DMSEM003, where it used to pass analysis and fail the generated C# (CS0019).
    /// </summary>
    [Test]
    [Arguments("Text", "P: policy { S < U }", "Text")]
    [Arguments("Boolean", "P: policy { F >= G }", "Boolean")]
    [Arguments("Binary", "Blob: Binary\n  Blob2: Binary\n  P: policy { Blob > Blob2 }", "Binary")]
    [Arguments("enum", "P: policy { L > M }", "enum Level")]
    [Arguments("entity", "P: policy { peer <= peer }", "entity Other")]
    [Arguments("value type", "Cmp: action (a: Money, b: Money) { if (a < b) { assign A to 1 } }", "value Money")]
    public async Task Ordering_OfAFamilyWithNoOrdering_IsDmsem003(string shape, string members, string described) {
        var result = GateParity.Check(OrderingDomain.Replace("MEMBERS", members, StringComparison.Ordinal));

        await Assert.That(result.Analysis).IsFalse().Because($"{shape}: {result}");
        await Assert.That(result.AnalysisSummary).Contains($"ordering is not defined for '{described}'").Because($"{shape}: {result}");
    }

    /// <summary>
    /// The rule's regression rows: what the provider orders stays accepted and agrees on every door (numbers, Decimal
    /// against Number, Date, Time, DateTime, Duration, Uuid, an optional operand, a <c>match</c> result, the clock
    /// values). Every row is accepted and runs on every door.
    /// </summary>
    [Test]
    [Arguments("Number", "P: policy { A < B and A >= B }", true)]
    [Arguments("Decimal against Number", "P: policy { Dc > A }", true)]
    [Arguments("Date", "P: policy { Due > Due }", true)]
    [Arguments("Date against Today", "P: policy { Due < Today }", true)]
    [Arguments("Time", "P: policy { Tm <= Tm }", true)]
    [Arguments("DateTime against Now", "P: policy { Stamp < Now }", true)]
    [Arguments("Duration", "P: policy { Gap <= Gap }", true)]
    [Arguments("Uuid", "P: policy { U1 < U2 }", true)]
    [Arguments("optional operand", "P: policy { Opt > A }", true)]
    [Arguments("match result", "P: policy { (match { A > 1 -> 1, else -> 2 }) > B }", true)]
    public async Task Ordering_WhatTheProviderOrders_StaysAcceptedByEveryDoor(string shape, string members, bool accepted) {
        var result = GateParity.Check(OrderingDomain.Replace("MEMBERS", members, StringComparison.Ordinal),
            SimStep.Create("Item", """{"A":1,"B":2,"S":"a","U":"b","F":true,"G":false,"L":"Low","M":"High","Due":"2026-01-01","Dc":1.5,"Tm":"10:00:00","Stamp":"2026-01-01T00:00:00Z","U1":"00000000-0000-0000-0000-000000000001","U2":"00000000-0000-0000-0000-000000000002"}""", "i"));

        await Assert.That(result.Analysis).IsEqualTo(accepted).Because($"{shape}: {result}");
        await Assert.That(result.Holds).IsTrue().Because($"{shape}: {result}");
    }

    /// <summary>
    /// The optional-operand row above, evaluated (T-06a): <c>Opt &gt; A</c> is the lifted comparison, false when Opt is
    /// absent and the ordering's answer when it is present, on the simulator and on the generated C#.
    /// </summary>
    [Test]
    [Arguments(null, "False")]
    [Arguments(5L, "True")]
    [Arguments(0L, "False")]
    public async Task Ordering_OfAnOptionalOperand_IsFalseWhenAbsent_OnBothHosts(long? opt, string answer) {
        var outcomes = await ParityScenario.FromDsl("""
            domain T
            OptOrderItem: entity {
              A: Number
              Opt: Number optional
              P: policy { Opt > A }
            }
            """, "OrderingOptionalOperand").AssertAgree(side => {
            if (opt is { } present)
                side.Create("OptOrderItem", ("A", 1L), ("Opt", present));
            else
                side.Create("OptOrderItem", ("A", 1L));
            side.EvaluatePolicy("P");
        });
        await Assert.That(outcomes[^1].State["P"]).IsEqualTo(answer);
    }

    private const string ValueEqualityDomain = """
        domain T
        Money: value {
          Amount: Number
        }
        Pair: value {
          X: Number
          Y: Number
        }
        Dec: value {
          D: Decimal
        }
        Acct: entity {
          N: Number
          Hit: policy { N == 1 }
          EqM: action (a: Money, b: Money) { if (a == b) { assign N to 1 } }
          NeM: action (a: Money, b: Money) { if (a != b) { assign N to 1 } }
          EqP: action (a: Pair, b: Pair) { if (a == b) { assign N to 1 } }
          EqD: action (a: Dec, b: Dec) { if (a == b) { assign N to 1 } }
        }
        """;

    private static bool SimulatedHit(string action, string json) {
        var surface = PolySurfaceHarness.New();
        var workspace = surface.Open("ValueEquality");
        var define = surface.Define(workspace, ValueEqualityDomain);
        if (!define.Success)
            throw new InvalidOperationException(define.Summary);
        var run = surface.Simulate(workspace, [
            SimStep.Create("Acct", """{"N":0}""", "a"), SimStep.Invoke("a", action, json), SimStep.Evaluate("Acct", "Hit", "a")]);
        return run.Step(1).Ok ? Evaluated(run.Step(2)) : throw new InvalidOperationException(run.Step(1).Summary);
    }

    private static string PrintedN(string action, Dictionary<string, object?> a, Dictionary<string, object?> b) {
        var (_, printed) = ParityScenario.FromDsl(ValueEqualityDomain, "ValueEquality").Run(side => {
            side.Create("Acct", ("N", 0L));
            side.Invoke(action, ("a", a), ("b", b));
        });
        return printed[^1].State["N"]!;
    }

    private static Dictionary<string, object?> Amount(long amount) => new() { ["Amount"] = amount };

    /// <summary>
    /// T-07a: a value type has value equality (the provider declares <c>==</c> and <c>!=</c> for it). The branch taken is
    /// observed, not acceptance: an equal pair and an unequal pair, each way round, on the simulator (the
    /// <c>poly_simulate</c> door) and on the generated C# (a record).
    /// </summary>
    [Test]
    [Arguments("EqM", 2L, 2L, true)]
    [Arguments("EqM", 2L, 3L, false)]
    [Arguments("NeM", 2L, 2L, false)]
    [Arguments("NeM", 2L, 3L, true)]
    public async Task ValueType_Equality_TakesTheBranchByValueOnEveryDoor(string action, long left, long right, bool taken) {
        await Assert.That(GateParity.Check(ValueEqualityDomain).Holds).IsTrue();
        await Assert.That(SimulatedHit(action, "{\"a\":{\"Amount\":" + left + "},\"b\":{\"Amount\":" + right + "}}")).IsEqualTo(taken);
        await Assert.That(PrintedN(action, Amount(left), Amount(right))).IsEqualTo(taken ? "1" : "0");
    }

    /// <summary>
    /// Known wrong, owner X-06 (value-type payloads built through the lowered factory): the simulator carries a value-type
    /// argument as the JSON text of the payload (<c>RuntimeTool.JsonElementToValue</c>, kept as is by
    /// <c>ModuleInstanceExecutor.Coercion</c>) and compares it with <c>object.Equals</c> (<c>AbiEmitter.Expressions</c>), so
    /// two equal values written with the fields in another order, or <c>2</c> against <c>2.0</c> for a Decimal field, are
    /// unequal there and equal on the generated C#. Whitespace is not a difference (the surface re-serialises compactly).
    /// A .NET caller that passes equal values as dictionaries to <c>ModuleInstanceExecutor.Invoke</c> gets them unequal on
    /// the simulator too.
    /// </summary>
    [Test]
    [Arguments("EqP", """{"a":{"X":1,"Y":2},"b":{"X":1,"Y":2}}""", """{"a":{"X":1,"Y":2},"b":{"Y":2,"X":1}}""")]
    [Arguments("EqD", """{"a":{"D":2},"b":{"D":2}}""", """{"a":{"D":2},"b":{"D":2.0}}""")]
    public async Task ValueType_EqualityOfEqualValuesWrittenDifferently_DiffersOnTheSimulator_UntilX06(string action, string sameText, string differentText) {
        await Assert.That(SimulatedHit(action, sameText)).IsTrue();
        await Assert.That(SimulatedHit(action, differentText)).IsFalse();
        static Dictionary<string, object?> Fields(params (string Name, object Value)[] fields) => fields.ToDictionary(f => f.Name, f => (object?)f.Value);
        var (left, right) = action == "EqP"
            ? (Fields(("X", 1L), ("Y", 2L)), Fields(("Y", 2L), ("X", 1L)))
            : (Fields(("D", 2m)), Fields(("D", 2.0m)));
        await Assert.That(PrintedN(action, left, right)).IsEqualTo("1");
    }

    private static string DateDocument(string members) => $$"""
        domain T
        uses temporal
        Part: entity {
          Due: Date
          Stamp: DateTime
        }
        Item: entity {
          A: Number
          Due: Date
          Stamp: DateTime
          Gap: Duration optional
          part: Part optional
          {{members}}
        }
        """;

    /// <summary>
    /// Review F1 (fix round 1). A date minus a date is a difference only as two properties: that is the shape lowering
    /// rewrites to <c>left.Subtract(right)</c> (<c>TemporalLowering.DateAddDaysLowering</c>). Any other shape lowers
    /// to a raw subtraction, and the VM refuses it. With the analyzer's exemption widened to every date-typed operand
    /// (this round, then reverted), GateParity recorded these rows: <c>part Stamp - Stamp</c> and
    /// <c>(Stamp + A) - Stamp</c> failed <c>polyc check</c> ("arithmetic operand is not numeric (got 'DateTime' and
    /// 'DateTime')"), and <c>part Due - Due</c> also failed the generated C# (CS0019 on <c>DateOnly - DateOnly</c>).
    /// So analysis rejects them, and the two-property DateTime control passes every door. T-10b owns deciding a date
    /// difference on types, in analysis and lowering together (removing row of the two-property shape rule).
    /// </summary>
    [Test]
    [Arguments("the review's repro", "Ok: policy { (part Due - Due) == (Due - Due) }", false)]
    [Arguments("a path-prefixed operand", "Close: action { assign Gap to part Stamp - Stamp }", false)]
    [Arguments("a parenthesised operand", "Close: action { assign Gap to (Stamp + A) - Stamp }", false)]
    [Arguments("control: two DateTime properties", "Close: action { assign Gap to Stamp - Stamp }", true)]
    public async Task DateDifference_AnalysisAcceptsOnlyTheShapeLoweringLowers_UntilT10b(string row, string members, bool accepted) {
        var result = GateParity.Check(DateDocument(members),
            SimStep.Create("Item", """{"A":1,"Due":"2026-01-01","Stamp":"2026-01-01T00:00:00Z"}""", "i"));

        await Assert.That(result.Analysis).IsEqualTo(accepted).Because($"{row}: {result}");
        await Assert.That(result.Holds).IsTrue().Because($"{row}: {result}");
    }

    private static IReadOnlyList<string> Errors(string members) =>
        [.. new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(DateDocument(members)).Parse()).Analysis.Diagnostics
            .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
            .Select(d => $"{d.Code}: {d.Message}")];

    /// <summary>
    /// Closing round, item 1: a same-family date difference of another shape is rejected with the reason, the shape
    /// lowering supports, naming the two operands the user wrote, not "arithmetic operand is not numeric".
    /// </summary>
    [Test]
    public async Task DateDifference_OfAnotherShape_SaysOnlyTwoPropertiesAreSupported() {
        await Assert.That(string.Join(" | ", Errors("Close: action { assign Gap to part Stamp - Stamp }"))).IsEqualTo(
            "DMSEM003: a date difference is supported only between two properties of the same entity " +
            "(got 'part Stamp' - 'Stamp')");
        await Assert.That(string.Join(" | ", Errors("Close: action { assign Gap to (Stamp + A) - Stamp }"))).IsEqualTo(
            "DMSEM003: a date difference is supported only between two properties of the same entity " +
            "(got '(Stamp + A)' - 'Stamp')");
        await Assert.That(Errors("Close: action { assign Gap to Stamp - Stamp }").Count).IsEqualTo(0);
    }

    private static string OffsetDocument(string members) => $$"""
        domain T
        uses temporal
        Part: entity {
          Due: Date
          Stamp: DateTime
        }
        Item: entity {
          A: Number
          D: Decimal
          Due: Date
          Due2: Date
          Stamp: DateTime
          Stamp2: DateTime
          Len: Duration
          OptDue: Date optional
          OptStamp: DateTime optional
          OptA: Number optional
          OptD: Decimal optional
          Opens: Time
          OutM: Time optional
          OutD: Date optional
          OutT: DateTime optional
          Gap: Duration optional
          part: Part optional
          {{members}}
        }
        """;

    private const string OffsetItem =
        """{"A":1,"D":2.5,"Due":"2026-01-01","Due2":"2026-01-05","Stamp":"2026-01-01T00:00:00Z","Stamp2":"2026-01-02T00:00:00Z","Len":"01:00:00","Opens":"09:00:00","OptDue":"2026-02-01","OptStamp":"2026-02-01T00:00:00Z","OptA":2,"OptD":1.5}""";

    /// <summary>
    /// T-10a: every temporal expression a DSL author can write means the same on the simulator, the generated C# and
    /// <c>polyc check</c>, or is a positioned analysis error. An empty <c>refusal</c> row is a GateParity pass (analysis
    /// accepts, every door runs it); any other row is refused at analysis and its message contains the text. Probed on
    /// the base, <c>origin/stream/types</c> @ 0235f244: the refused rows failed on at least one door or the doors
    /// disagreed (the C# did not compile where the simulator ran).
    /// </summary>
    [Test]
    // runs on every door today
    [Arguments("Date + literal", "Go: action { assign OutD to Due + 3 Days }", null, "")]
    [Arguments("Date - literal", "Go: action { assign OutD to Due - 3 Days }", null, "")]
    [Arguments("DateTime + literal", "Go: action { assign OutT to Stamp + 3 Hours }", null, "")]
    [Arguments("Today + literal", "Go: action { assign OutD to Today + 3 Days }", null, "")]
    [Arguments("Now + literal", "Go: action { assign OutT to Now + 3 Hours }", null, "")]
    [Arguments("Date + Number", "Go: action { assign OutD to Due + A }", null, "")]
    [Arguments("Date - Number", "Go: action { assign OutD to Due - A }", null, "")]
    [Arguments("DateTime + Number", "Go: action { assign OutT to Stamp + A }", null, "")]
    [Arguments("DateTime - Number", "Go: action { assign OutT to Stamp - A }", null, "")]
    [Arguments("Date + integer literal", "Go: action { assign OutD to Due + 1 }", null, "")]
    [Arguments("Date + optional Number", "Go: action { assign OutD to Due + OptA }", null, "")]
    [Arguments("Date parameter + Number", "Go: action (s: Date, n: Number) { assign OutD to s + n }", """{"s":"2026-01-01","n":2}""", "")]
    [Arguments("Date parameter + literal", "Go: action (s: Date) { assign OutD to s + 3 Days }", """{"s":"2026-01-01"}""", "")]
    [Arguments("Duration assign (R1)", "Go: action { assign Len to 30 Minutes }", null, "")]
    [Arguments("optional Duration assign (R1)", "Go: action { assign Gap to 2 Hours }", null, "")]
    [Arguments("Duration compared (R1)", "Go: policy { Len > 1 Hours }", null, "")]
    [Arguments("the duration on the left (R1)", "Go: policy { 1 Hours < Len }", null, "")]
    [Arguments("Duration equality (R1)", "Go: policy { Len == 60 Minutes }", null, "")]
    [Arguments("DateTime difference compared with a duration", "Go: policy { (Stamp2 - Stamp) > 1 Hours }", null, "")]
    [Arguments("two duration literals compared", "Go: policy { 1 Hours < 2 Hours }", null, "")]
    [Arguments("Date + a calendar literal (unchanged)", "Go: action { assign OutD to Due + 2 Months }", null, "")]
    [Arguments("Time + a literal (unchanged)", "Go: action { assign OutM to Opens + 2 Hours }", null, "")]
    [Arguments("Time - a literal (unchanged)", "Go: action { assign OutM to Opens - 30 Minutes }", null, "")]
    [Arguments("a negative duration literal does not parse", "Go: action { assign Len to -5 Minutes }", null, "Parse error")]
    [Arguments("DateTime - DateTime", "Go: action { assign Gap to Stamp - Stamp2 }", null, "")]
    [Arguments("Date - Date (R6)", "Go: action { assign Gap to Due2 - Due }", null, "")]
    [Arguments("Date - Date in a policy (R6)", "Go: policy { (Due - Due2) == (Due - Due2) }", null, "")]
    [Arguments("Date parameters - Date parameters (R6)", "Go: action (s: Date, t: Date) { assign Gap to s - t }", """{"s":"2026-01-05","t":"2026-01-01"}""", "")]
    [Arguments("Date difference compared with a duration (R1, R6)", "Go: policy { (Due2 - Due) > 1 Days }", null, "")]
    // R3 (#648): an optional date has nothing to offset when it is unset
    [Arguments("optional Date + literal", "Go: action { assign OutD to OptDue + 3 Days }", null, "optional")]
    [Arguments("optional DateTime + literal", "Go: action { assign OutT to OptStamp + 3 Hours }", null, "optional")]
    [Arguments("optional Date + Number", "Go: action { assign OutD to OptDue + A }", null, "optional")]
    [Arguments("optional DateTime + Number", "Go: action { assign OutT to OptStamp + A }", null, "optional")]
    [Arguments("optional Date - Date", "Go: action { assign Gap to OptDue - Due }", null, "optional")]
    [Arguments("optional DateTime - DateTime", "Go: action { assign Gap to OptStamp - Stamp }", null, "optional")]
    [Arguments("DateTime - optional DateTime", "Go: action { assign Gap to Stamp - OptStamp }", null, "optional")]
    [Arguments("optional Date + literal in a policy", "Go: policy { OptDue + 1 Days > Today }", null, "optional")]
    // R5 (OQ-7): a Duration value is F-11's
    [Arguments("DateTime + Duration value", "Go: action { assign OutT to Stamp + Len }", null, "F-11")]
    [Arguments("DateTime - Duration value", "Go: action { assign OutT to Stamp - Len }", null, "F-11")]
    [Arguments("Date + Duration value", "Go: action { assign OutD to Due + Len }", null, "F-11")]
    [Arguments("Date - Duration value", "Go: action { assign OutD to Due - Len }", null, "F-11")]
    [Arguments("DateTime + a difference", "Go: action { assign OutT to Stamp + (Stamp2 - Stamp) }", null, "F-11")]
    [Arguments("DateTime parameter + Duration parameter", "Go: action (s: DateTime, l: Duration) { assign OutT to s + l }", """{"s":"2026-01-01T00:00:00Z","l":"01:00:00"}""", "F-11")]
    [Arguments("Now + Duration value", "Go: action { assign OutT to Now + Len }", null, "F-11")]
    [Arguments("Today + Duration value", "Go: action { assign OutD to Today + Len }", null, "F-11")]
    // R4 (owner ruling 2026-11-13): every Decimal day count is refused. Date + Decimal ran and silently dropped the
    // fraction; DateTime + Decimal and the non-property lefts failed on a host.
    [Arguments("Date + Decimal property", "Go: action { assign OutD to Due + D }", null, "Decimal day count")]
    [Arguments("Date - Decimal property", "Go: action { assign OutD to Due - D }", null, "Decimal day count")]
    [Arguments("Date + Decimal literal", "Go: action { assign OutD to Due + 1.5 }", null, "Decimal day count")]
    [Arguments("Date - Decimal literal", "Go: action { assign OutD to Due - 1.5 }", null, "Decimal day count")]
    [Arguments("Date parameter + Decimal parameter", "Go: action (s: Date, n: Decimal) { assign OutD to s + n }", """{"s":"2026-01-01","n":2.5}""", "Decimal day count")]
    [Arguments("Date parameter - Decimal property", "Go: action (s: Date) { assign OutD to s - D }", """{"s":"2026-01-01"}""", "Decimal day count")]
    [Arguments("Date + optional Decimal", "Go: action { assign OutD to Due + OptD }", null, "Decimal day count")]
    [Arguments("DateTime + Decimal property", "Go: action { assign OutT to Stamp + D }", null, "Decimal day count")]
    [Arguments("DateTime - Decimal property", "Go: action { assign OutT to Stamp - D }", null, "Decimal day count")]
    [Arguments("DateTime + Decimal literal", "Go: action { assign OutT to Stamp + 1.5 }", null, "Decimal day count")]
    [Arguments("DateTime parameter + Decimal parameter", "Go: action (s: DateTime, n: Decimal) { assign OutT to s + n }", """{"s":"2026-01-01T00:00:00Z","n":2.5}""", "Decimal day count")]
    [Arguments("Now + Decimal", "Go: action { assign OutT to Now + D }", null, "Decimal day count")]
    [Arguments("Now - Decimal", "Go: action { assign OutT to Now - D }", null, "Decimal day count")]
    [Arguments("Today + Decimal", "Go: action { assign OutD to Today + D }", null, "Decimal day count")]
    [Arguments("Today - Decimal", "Go: action { assign OutD to Today - D }", null, "Decimal day count")]
    [Arguments("a date expression + Decimal", "Go: action { assign OutD to (Due + 1) + D }", null, "Decimal day count")]
    [Arguments("a path-prefixed Date + Decimal", "Go: action { assign OutD to part Due + D }", null, "Decimal day count")]
    // a count offset with a left that is not a property or parameter read: the simulator ran it, the C# did not compile
    [Arguments("Now + Number", "Go: action { assign OutT to Now + A }", null, "date property or parameter")]
    [Arguments("Now - Number", "Go: action { assign OutT to Now - A }", null, "date property or parameter")]
    [Arguments("Today + Number", "Go: action { assign OutD to Today + A }", null, "date property or parameter")]
    [Arguments("Today - Number", "Go: action { assign OutD to Today - A }", null, "date property or parameter")]
    [Arguments("Today + integer literal", "Go: action { assign OutD to Today + 1 }", null, "date property or parameter")]
    [Arguments("a date expression + Number", "Go: action { assign OutD to (Due + 1) + A }", null, "date property or parameter")]
    [Arguments("a chained integer offset", "Go: action { assign OutD to Due + 1 + 1 }", null, "date property or parameter")]
    [Arguments("a literal offset + Number", "Go: action { assign OutD to (Due + 3 Days) + A }", null, "date property or parameter")]
    [Arguments("a path-prefixed Date + Number", "Go: action { assign OutD to part Due + A }", null, "date property or parameter")]
    [Arguments("a path-prefixed DateTime + Number", "Go: action { assign OutT to part Stamp + A }", null, "date property or parameter")]
    public async Task TemporalOffset_EveryDoorAgreesOrAnalysisRefuses(string row, string members, string? args, string refusal) {
        var steps = members.Contains("policy", StringComparison.Ordinal)
            ? new[] { SimStep.Create("Item", OffsetItem, "i"), SimStep.Evaluate("Item", "Go", "i") }
            : [SimStep.Create("Item", OffsetItem, "i"), SimStep.Invoke("i", "Go", args)];
        var result = GateParity.Check(OffsetDocument(members), steps);

        if (refusal.Length == 0) {
            await Assert.That(result.Analysis).IsTrue().Because($"{row}: {result}");
            await Assert.That(result.Holds).IsTrue().Because($"{row}: {result}");
            return;
        }
        await Assert.That(result.Analysis).IsFalse().Because($"{row}: {result}");
        await Assert.That(result.AnalysisSummary).Contains(refusal).Because($"{row}: {result}");
    }

    /// <summary>R6: two Date properties subtracted lower to <c>TimeSpan.FromDays(l.DayNumber - r.DayNumber)</c>; both hosts answer the day count.</summary>
    [Test]
    public async Task DateOnlyDifference_BothHostsAnswerTheDayCount() {
        const string dsl = """
            domain Span
            uses temporal
            Job: entity {
              Started: Date
              Finished: Date
              Elapsed: Duration optional
              Close: action { assign Elapsed to Finished - Started }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "T10aDateDifference").AssertAgree(side => {
            side.Create("Job", ("Started", new DateOnly(2026, 1, 1)), ("Finished", new DateOnly(2026, 1, 5)));
            side.Invoke("Close");
        });

        await Assert.That(outcomes[^1].Success).IsTrue();
        await Assert.That(outcomes[^1].State["Elapsed"]).IsEqualTo(TimeSpan.FromDays(4).ToString());
    }

    /// <summary>
    /// T-06a's <c>ExprType.Optional</c> closes the path-read gap: a bare duration compared with an optional Duration reached
    /// through a to-one link is refused (#484), while the same read of a REQUIRED member runs on every door and stays accepted
    /// (a to-one read is "optional" in analysis only so that <c>== null</c> is accepted; it cannot be absent at run time).
    /// </summary>
    [Test]
    [Arguments("a required Duration through a to-one link", "Go: policy { advisor Len > 1 Hours }", "")]
    [Arguments("an optional Duration through a to-one link", "Go: policy { advisor Gap > 1 Hours }", "#484")]
    [Arguments("the duration on the left", "Go: policy { 1 Hours < advisor Gap }", "#484")]
    [Arguments("a Date offset read through a link", "Go: action { assign OutD to advisor Due + 3 Days }", "F-11")]
    public async Task OptionalDurationThroughALink_IsRefused_RequiredOneRuns(string row, string members, string refusal) {
        var dsl = $$"""
            domain T
            uses temporal
            Person: entity {
              Due: Date
              Len: Duration
              Gap: Duration optional
            }
            Job: entity {
              advisor: Person
              OutD: Date optional
              {{members}}
            }
            """;
        const string person = """{"Due":"2026-01-01","Len":"01:00:00","Gap":"02:00:00"}""";
        SimStep[] steps = [
            SimStep.Create("Person", person, "p"), SimStep.Create("Job", null, "j"), SimStep.Link("j", "advisor", "p"),
            members.Contains("policy", StringComparison.Ordinal) ? SimStep.Evaluate("Job", "Go", "j") : SimStep.Invoke("j", "Go"),
        ];
        var result = GateParity.Check(dsl, steps);

        if (refusal.Length == 0) {
            await Assert.That(result.Analysis).IsTrue().Because($"{row}: {result}");
            await Assert.That(result.Holds).IsTrue().Because($"{row}: {result}");
            return;
        }
        await Assert.That(result.Analysis).IsFalse().Because($"{row}: {result}");
        await Assert.That(result.AnalysisSummary).Contains(refusal).Because($"{row}: {result}");
    }

    /// <summary>R6: the earlier date on the left is a negative difference, the same on both hosts.</summary>
    [Test]
    public async Task DateOnlyDifference_Negative_BothHostsAnswerMinusTheDayCount() {
        const string dsl = """
            domain Span
            uses temporal
            Job: entity {
              Started: Date
              Finished: Date
              Elapsed: Duration optional
              Close: action { assign Elapsed to Finished - Started }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "T10aDateDifferenceNegative").AssertAgree(side => {
            side.Create("Job", ("Started", new DateOnly(2026, 1, 5)), ("Finished", new DateOnly(2026, 1, 1)));
            side.Invoke("Close");
        });

        await Assert.That(outcomes[^1].State["Elapsed"]).IsEqualTo(TimeSpan.FromDays(-4).ToString());
    }

    /// <summary>
    /// The lowering of a bare duration runs on the VM and in the printed C# for every unit with a fixed length: the same
    /// <c>TimeSpan</c>, computed here from the platform.
    /// </summary>
    [Test]
    [Arguments("Milliseconds", 1500L)]
    [Arguments("Seconds", 90L)]
    [Arguments("Minutes", 30L)]
    [Arguments("Hours", 5L)]
    [Arguments("Days", 3L)]
    [Arguments("Weeks", 2L)]
    public async Task BareDuration_EveryFixedUnit_BothHostsAssignTheSameTimeSpan(string unit, long amount) {
        var dsl = $$"""
            domain Sla
            uses temporal
            Job: entity {
              Budget: Duration optional
              Set: action { assign Budget to {{amount}} {{unit}} }
            }
            """;
        var expected = unit switch {
            "Milliseconds" => TimeSpan.FromMilliseconds(amount),
            "Seconds" => TimeSpan.FromSeconds(amount),
            "Minutes" => TimeSpan.FromMinutes(amount),
            "Hours" => TimeSpan.FromHours(amount),
            "Days" => TimeSpan.FromDays(amount),
            _ => TimeSpan.FromDays(amount * 7),
        };
        var outcomes = await ParityScenario.FromDsl(dsl, "T10aUnit" + unit).AssertAgree(side => {
            side.Create("Job");
            side.Invoke("Set");
        });

        await Assert.That(outcomes[^1].Success).IsTrue().Because(unit);
        await Assert.That(outcomes[^1].State["Budget"]).IsEqualTo(expected.ToString()).Because(unit);
    }

    /// <summary>
    /// The product round trip the round-trip matrix cannot make: define, export the DSL, define that text in a fresh workspace,
    /// export again. The two texts and the two models are equal, and the reread document runs.
    /// </summary>
    [Test]
    public async Task BareDurationAssign_ProductRoundTrip_RereadsAsTheSameModel() {
        const string dsl = """
            domain Sla
            uses temporal
            Job: entity {
              Len: Duration
              Fast: policy { Len < 1 Hours }
              Set: action { assign Len to 30 Minutes }
            }
            """;
        var surface = PolySurfaceHarness.New();
        var first = surface.Open("RoundTrip1");
        await Assert.That(surface.Define(first, dsl).Success).IsTrue();
        var text = surface.Export(first, "dsl").Content;
        var second = surface.Open("RoundTrip2");
        await Assert.That(surface.Define(second, text).Success).IsTrue().Because(text);

        await Assert.That(surface.Export(second, "dsl").Content).IsEqualTo(text);
        await Assert.That(surface.Inspect(second, detail: "full").Summary).IsEqualTo(surface.Inspect(first, detail: "full").Summary);
        await Assert.That(text).Contains("assign Len to 30 Minutes").And.Contains("Len < 1 Hours");
        var run = surface.Simulate(second, SimStep.Create("Job", """{"Len":"02:00:00"}""", "j"), SimStep.Invoke("j", "Set"), SimStep.Evaluate("Job", "Fast", "j"));
        await Assert.That(run.Success).IsTrue().Because(run.Summary);
    }

    /// <summary>R1: a bare duration assigned to a Duration property and compared with one answers the same value on both hosts.</summary>
    [Test]
    public async Task BareDuration_AssignedAndCompared_BothHostsAnswerTheValue() {
        const string dsl = """
            domain Sla
            uses temporal
            Job: entity {
              Budget: Duration
              Fast: policy { Budget < 1 Hours }
              Set: action { assign Budget to 30 Minutes }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "T10aDuration").AssertAgree(side => {
            side.Create("Job", ("Budget", TimeSpan.FromHours(2)));
            side.EvaluatePolicy("Fast");
            side.Invoke("Set");
            side.EvaluatePolicy("Fast");
        });

        await Assert.That(outcomes[1].State["Fast"]).IsEqualTo("False");
        await Assert.That(outcomes[3].State["Fast"]).IsEqualTo("True");
        await Assert.That(outcomes[3].State["Budget"]).IsEqualTo(TimeSpan.FromMinutes(30).ToString());
    }

    /// <summary>
    /// Probed on the base: <c>default(Now + 7 Days)</c> analysed clean and the simulator ran it, but the generated C#
    /// did not compile (CS1737, a default the printed factory cannot carry). No default lowering exists for a date
    /// operation, so analysis refuses it; <c>default(Now)</c> and <c>default(Today)</c> keep working.
    /// </summary>
    [Test]
    [Arguments("Stamp3: DateTime default(Now + 7 Days)", "no default lowering")]
    [Arguments("Stamp3: Date default(Today + 7 Days)", "no default lowering")]
    [Arguments("Stamp3: DateTime optional default(Now + 7 Days)", "no default lowering")]
    [Arguments("Stamp3: DateTime default(Now)", "")]
    public async Task DefaultOfADateOperation_IsRefusedAtAnalysis_ClockDefaultsStay(string members, string refusal) {
        var result = GateParity.Check(OffsetDocument(members), SimStep.Create("Item", OffsetItem, "i"));

        if (refusal.Length == 0) {
            await Assert.That(result.Holds).IsTrue().Because(result.ToString());
            await Assert.That(result.Analysis).IsTrue().Because(result.ToString());
            return;
        }
        await Assert.That(result.Analysis).IsFalse().Because(result.ToString());
        await Assert.That(result.AnalysisSummary).Contains(refusal).Because(result.ToString());
    }

    private static (Domain Domain, SumExpr Sum) SumDomain() {
        var sum = (SumExpr)DomainExpression.Sum("items", "Amount");
        var item = new Entity("Item", [new Property("Amount", new DomainTypeReference("Decimal"), [])], [], [], []);
        var host = new Entity("Host", [],
            [], [new Policy("Big", DomainExpression.GreaterThan(sum, DomainExpression.Literal(100L)))], []);
        var domain = DomainTestFactory.Create("T", [
            new DmPrimitive("Decimal", PrimitiveSemantics.Decimal, []), item, host,
        ], [new Relationship("items", new DomainTypeReference("Host"), new DomainTypeReference("Item"), RelationshipCardinality.OneToMany, [])]);
        return (domain, sum);
    }

    /// <summary>The seed lowering gives the sum's accumulator, read from the lowered policy body.</summary>
    private static object? LoweredSeed(Domain domain, AnalysisResult analysis, SumExpr sum) {
        var lowered = new DomainExpressionLoweringPass(new LoweringContext(
                new Poly.Ast.Nodes.Parameter("this"), analysis, domain, SourceEntityName: "Host"))
            .LowerExpression(sum, new Poly.Ast.Nodes.Parameter("this"));
        var seed = lowered.Statements.OfType<Assignment>().FirstOrDefault()
            ?? throw new InvalidOperationException("The lowered sum has no accumulator assignment.");
        return seed.Value is Constant constant
            ? constant.Value
            : throw new InvalidOperationException($"The sum's seed is not a constant: {seed.Value}.");
    }

    /// <summary>
    /// Fork B (honoured): a pack that maps <c>Decimal</c> to <c>double</c> changes the sum's type for analysis and
    /// lowering alike. Analysis types <c>sum items Amount</c> as Float64 and lowering seeds the accumulator 0.0.
    /// </summary>
    [Test]
    public async Task Sum_UnderClrOverride_AnalysisAndLoweringAgree() {
        var (domain, sum) = SumDomain();
        var builder = SessionBuilder.CreateEmpty().LoadCore();
        builder.TypeMaps.OverrideClrTypeName("Decimal", "double");
        var analysis = builder.Build().Analyze(domain);

        var typed = analysis.GetMetadata<TypedExpressionMetadata>(sum);
        await Assert.That(typed).IsNotNull().Because("analysis states the sum's type on the node");
        await Assert.That(typed!.Type.Definition?.PrimitiveType).IsEqualTo(PrimType.Float64);
        await Assert.That(LoweredSeed(domain, analysis, sum)).IsEqualTo(0.0);
    }

    /// <summary>Without an override the same sum is Decimal for both, and the seed is 0m.</summary>
    [Test]
    public async Task Sum_WithoutOverride_AnalysisAndLoweringAgreeOnDecimal() {
        var (domain, sum) = SumDomain();
        var analysis = SessionBuilder.CreateEmpty().LoadCore().Build().Analyze(domain);

        var typed = analysis.GetMetadata<TypedExpressionMetadata>(sum);
        await Assert.That(typed).IsNotNull().Because("analysis states the sum's type on the node");
        await Assert.That(typed!.Type.Definition?.PrimitiveType).IsEqualTo(PrimType.Decimal);
        await Assert.That(LoweredSeed(domain, analysis, sum)).IsEqualTo(0m);
    }

    /// <summary>
    /// T-05b (row T-05's red test): an expression the analyzer cannot type is an analysis error, DMSEM006, not silently
    /// accepted; on master this document passes analysis and the VM refuses it. The typed control passes every door.
    /// </summary>
    [Test]
    public async Task UnresolvableExpression_IsAnalysisError() {
        const string dsl = """
            domain T
            Book: entity {
              Qty: Number default(2)
            }
            Order: entity {
              N: Number default(0)
              books: many Book
              book: Book optional
              Go: action { assign N to READ }
            }
            """;
        var untyped = GateParity.Check(dsl.Replace("READ", "books Qty", StringComparison.Ordinal),
            SimStep.Create("Order", alias: "o"), SimStep.Invoke("o", "Go"));
        await Assert.That(untyped.Analysis).IsFalse().Because(untyped.ToString());
        await Assert.That(untyped.AnalysisSummary).Contains("cannot determine the type of 'books Qty'");

        var typed = GateParity.Check(dsl.Replace("READ", "book Qty", StringComparison.Ordinal), SimStep.Create("Order", alias: "o"));
        await Assert.That(typed.Analysis).IsTrue().Because(typed.ToString());
        await Assert.That(typed.Holds).IsTrue().Because(typed.ToString());
    }
}