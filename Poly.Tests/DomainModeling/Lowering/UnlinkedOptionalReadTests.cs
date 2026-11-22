using Poly.DomainModeling;
using Poly.DomainModeling.Lowering;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #936: a read through an <c>optional</c> to-one link that is not linked yields <em>absent</em>, and every
/// position treats absent the way the guide's optional-value rule does: a comparison follows C#'s lifted
/// rules (<c>==</c> true only when both are absent, <c>!=</c> true when exactly one is, an ordering false),
/// an optional Boolean in a condition is <c>is true</c>, an assign to an optional target stores absent, and an
/// assign to a required target, or an <c>invoke</c> on the unlinked hop, fails <c>Required</c>. Each row is a
/// position from #936, asserted on the simulator and the printed C# (<c>AssertAgree</c>) and then against the
/// documented answer itself.
/// </summary>
public class UnlinkedOptionalReadTests {
    const string PolicyDsl = """
        domain Grid
        uses temporal
        Advisor: entity {
          Name: Text
          Score: Number
          Active: Boolean default(false)
          Due: Date optional
        }
        Customer: entity {
          Name: Text
          Ready: Boolean default(true)
          advisor: Advisor optional
          NameIsX: policy { advisor Name is "x" }
          NameIsNotX: policy { advisor Name is not "x" }
          ScoreOver1: policy { advisor Score > 1 }
          ScoreNotOne: policy { advisor Score != 1 }
          ArithOver: policy { advisor Score + 1 > 2 }
          ActiveNow: policy { advisor Active }
          NotActive: policy { not (advisor Active) }
          NameAbsent: policy { advisor Name is null }
          NamePresent: policy { advisor Name is not null }
          DueAbsent: policy { advisor Due is null }
        }
        """;

    [Test]
    public async Task Policy_UnlinkedOptionalHop_AnswersTheAbsentRule_OnBothHosts() {
        var scenario = ParityScenario.FromDsl(PolicyDsl, "UnlinkedPolicyGrid");
        var outcomes = await scenario.AssertAgree(side => {
            side.Create("Customer", ("Name", "Ada"), ("Ready", true));
            foreach (var policy in new[] {
                "NameIsX", "NameIsNotX", "ScoreOver1", "ScoreNotOne", "ArithOver",
                "ActiveNow", "NotActive", "NameAbsent", "NamePresent", "DueAbsent"
            })
                side.EvaluatePolicy(policy);
        });

        // No advisor: every read through the hop is absent. == is false against a present "x", != is true,
        // an ordering and arithmetic are false, a bare optional Boolean is not true, and `is null` holds.
        await AssertAnswers(outcomes, new() {
            ["NameIsX"] = false,
            ["NameIsNotX"] = true,
            ["ScoreOver1"] = false,
            ["ScoreNotOne"] = true,
            ["ArithOver"] = false,
            ["ActiveNow"] = false,
            ["NotActive"] = true,
            ["NameAbsent"] = true,
            ["NamePresent"] = false,
            ["DueAbsent"] = true,
        });

        // A linked advisor with a name, a score and Active: the same reads answer the real values.
        var outcomes2 = await scenario.AssertAgree(side => {
            var advisor = side.Create("Advisor", ("Name", "Pat"), ("Score", 2L), ("Active", true));
            side.Create("Customer", ("Name", "Ada"), ("Ready", true), ("advisor", advisor));
            foreach (var policy in new[] {
                "NameIsX", "NameIsNotX", "ScoreOver1", "ScoreNotOne", "ArithOver",
                "ActiveNow", "NotActive", "NameAbsent", "NamePresent", "DueAbsent"
            })
                side.EvaluatePolicy(policy);
        });

        await AssertAnswers(outcomes2, new() {
            ["NameIsX"] = false,
            ["NameIsNotX"] = true,
            ["ScoreOver1"] = true,
            ["ScoreNotOne"] = true,
            ["ArithOver"] = true,
            ["ActiveNow"] = true,
            ["NotActive"] = false,
            ["NameAbsent"] = false,
            ["NamePresent"] = true,
            ["DueAbsent"] = true,
        });
    }

    /// <summary>
    /// An absent operand on each side is <c>==</c> and not <c>!=</c>, as two absent optional values are — one side
    /// an unlinked hop, the other an omitted optional property. (A comparison of two hop reads is refused at
    /// analysis: the right operand can only resolve on the comparison's own subject.)
    /// </summary>
    [Test]
    public async Task Comparison_BothSidesAbsent_IsEqualAndNotUnequal_OnBothHosts() {
        const string dsl = """
            domain Grid
            Advisor: entity { Score: Number optional }
            Customer: entity {
              Seen: Number optional
              left: Advisor optional
              SameScore: policy { Seen is left Score }
              DifferentScore: policy { Seen is not left Score }
            }
            """;
        var scenario = ParityScenario.FromDsl(dsl, "UnlinkedBothSides");

        var bothUnlinked = await scenario.AssertAgree(side => {
            side.Create("Customer");
            side.EvaluatePolicy("SameScore");
            side.EvaluatePolicy("DifferentScore");
        });

        await Assert.That(Step(bothUnlinked, "policy SameScore").State["SameScore"]).IsEqualTo("True")
            .Because(Dump(bothUnlinked));
        await Assert.That(Step(bothUnlinked, "policy DifferentScore").State["DifferentScore"]).IsEqualTo("False");

        // The link is set but the leaf is absent: both sides are still absent, so the answers do not move.
        var leafAbsent = await scenario.AssertAgree(side => {
            var advisor = side.Create("Advisor");
            side.Create("Customer", ("left", advisor));
            side.EvaluatePolicy("SameScore");
            side.EvaluatePolicy("DifferentScore");
        });

        await Assert.That(Step(leafAbsent, "policy SameScore").State["SameScore"]).IsEqualTo("True")
            .Because(Dump(leafAbsent));
        await Assert.That(Step(leafAbsent, "policy DifferentScore").State["DifferentScore"]).IsEqualTo("False");
    }

    const string GuardDsl = """
        domain Gate
        Advisor: entity { Score: Number }
        Customer: entity {
          Seen: Number optional
          advisor: Advisor optional
          ScoreOver1: policy { advisor Score > 1 }
          Ask: action require ScoreOver1 { assign Seen to 1 }
          AskNot: action require not ScoreOver1 { assign Seen to 2 }
        }
        """;

    /// <summary>A <c>require</c> over an unlinked hop is a false predicate: the action is refused with <c>Policy</c>, not <c>Required</c>.</summary>
    [Test]
    public async Task Guard_UnlinkedOptionalHop_RefusesWithPolicy_NotRequired() {
        var scenario = ParityScenario.FromDsl(GuardDsl, "UnlinkedGuardGrid");
        var outcomes = await scenario.AssertAgree(side => {
            side.Create("Customer");
            side.Invoke("Ask");
        });

        var refused = outcomes[^1];
        await Assert.That(refused.Success).IsFalse();
        await Assert.That(refused.FailureKind).IsEqualTo("Policy");
        await Assert.That(refused.FailureGuard).IsEqualTo("ScoreOver1");
        await Assert.That(refused.Message).Contains("blocked by policy 'ScoreOver1'");
        await Assert.That(refused.State["Seen"]).IsNull();
    }

    /// <summary>
    /// The documented negation rule: <c>not (absent &gt; 1)</c> is true, exactly as <c>not (OptDue &lt; Today)</c>
    /// is true while the optional date is absent, so <c>require not P</c> runs where <c>require P</c> refuses.
    /// </summary>
    [Test]
    public async Task GuardNot_UnlinkedOptionalHop_Runs() {
        var scenario = ParityScenario.FromDsl(GuardDsl, "UnlinkedGuardNotGrid");
        var outcomes = await scenario.AssertAgree(side => {
            side.Create("Customer");
            side.Invoke("AskNot");
        });

        await Assert.That(outcomes[^1].Success).IsTrue()
            .Because($"{outcomes[^1].FailureKind} {outcomes[^1].Message}");
        await Assert.That(outcomes[^1].State["Seen"]).IsEqualTo("2");
    }

    const string AssignDsl = """
        domain Copy
        Advisor: entity {
          Score: Number
          Name: Text
        }
        Customer: entity {
          Seen: Number optional
          MustScore: Number
          Note: Text optional
          advisor: Advisor optional
          Snapshot: action { assign Seen to advisor Score }
          SnapshotText: action { assign Note to advisor Name }
          SnapshotArith: action { assign Seen to advisor Score + 1 }
          Force: action { assign MustScore to advisor Score }
        }
        """;

    /// <summary>
    /// An assign from an unlinked hop stores absent in an optional target, on both hosts. The Text target is the
    /// #936 example (P-13b); an optional <em>Number</em> target is blocked in the simulator, which stores <c>0</c>
    /// for a null written through a branch while the printed module stores null (reported on #936).
    /// </summary>
    [Test]
    public async Task Assign_ToOptionalTarget_UnlinkedOptionalHop_StoresAbsent() {
        var scenario = ParityScenario.FromDsl(AssignDsl, "UnlinkedAssignOptional");
        var outcomes = await scenario.AssertAgree(side => {
            side.Create("Customer", ("MustScore", 7L));
            side.Invoke("SnapshotText");
        });

        await Assert.That(outcomes[^1].Success).IsTrue()
            .Because($"{outcomes[^1].FailureKind} {outcomes[^1].Message}");
        await Assert.That(outcomes[^1].State["Note"]).IsNull();
        await Assert.That(outcomes[^1].State["MustScore"]).IsEqualTo("7");
    }

    /// <summary>An assign from an unlinked hop to a required target fails <c>Required</c> and leaves the target as it was.</summary>
    [Test]
    public async Task Assign_ToRequiredTarget_UnlinkedOptionalHop_FailsRequired() {
        var scenario = ParityScenario.FromDsl(AssignDsl, "UnlinkedAssignRequired");
        var outcomes = await scenario.AssertAgree(side => {
            side.Create("Customer", ("MustScore", 7L));
            side.Invoke("Force");
        });

        var refused = outcomes[^1];
        await Assert.That(refused.Success).IsFalse();
        await Assert.That(refused.FailureKind).IsEqualTo("Required");
        await Assert.That(refused.FailurePath).IsEqualTo("Customer.MustScore");
        await Assert.That(refused.Message).Contains("'MustScore' is required.");
        await Assert.That(refused.State["MustScore"]).IsEqualTo("7");
    }

    /// <summary>A linked hop writes the real value into the same targets.</summary>
    [Test]
    public async Task Assign_LinkedHop_WritesTheValue_OnBothHosts() {
        var scenario = ParityScenario.FromDsl(AssignDsl, "LinkedAssignControl");
        var outcomes = await scenario.AssertAgree(side => {
            var advisor = side.Create("Advisor", ("Score", 5L), ("Name", "Pat"));
            side.Create("Customer", ("MustScore", 7L), ("advisor", advisor));
            side.Invoke("Snapshot");
            side.Invoke("SnapshotText");
            side.Invoke("SnapshotArith");
            side.Invoke("Force");
        });

        await Assert.That(outcomes[^1].Success).IsTrue();
        await Assert.That(outcomes[^1].State["Seen"]).IsEqualTo("6");
        await Assert.That(outcomes[^1].State["Note"]).IsEqualTo("Pat");
        await Assert.That(outcomes[^1].State["MustScore"]).IsEqualTo("5");
    }

    /// <summary>A quantifier body that reads a related record's unlinked hop is false, and true once that hop is linked.</summary>
    [Test]
    public async Task QuantifierBody_ItemHopUnlinked_IsFalse_OnBothHosts() {
        const string dsl = """
            domain Quant
            Advisor: entity { Score: Number }
            Contact: entity { advisor: Advisor optional }
            Customer: entity {
              contacts: many Contact
              Flagged: policy { any contacts where advisor Score > 1 }
            }
            """;
        var scenario = ParityScenario.FromDsl(dsl, "UnlinkedQuantifier");

        var unlinked = await scenario.AssertAgree(side => {
            var advisor = side.Create("Advisor", ("Score", 5L));
            var contact = side.Create("Contact");
            side.Create("Customer", ("contacts", new[] { contact }));
            side.EvaluatePolicy("Flagged");
        });
        await Assert.That(unlinked[^1].State["Flagged"]).IsEqualTo("False");

        var linked = await scenario.AssertAgree(side => {
            var advisor = side.Create("Advisor", ("Score", 5L));
            var contact = side.Create("Contact", ("advisor", advisor));
            side.Create("Customer", ("contacts", new[] { contact }));
            side.EvaluatePolicy("Flagged");
        });
        await Assert.That(linked[^1].State["Flagged"]).IsEqualTo("True");
    }

    /// <summary>
    /// An <c>invoke</c> on an unlinked hop keeps its <c>Required</c> refusal: there is no value to run the action
    /// on, and a silent no-op would skip an effect the document asked for.
    /// </summary>
    [Test]
    public async Task Invoke_UnlinkedOptionalHop_StaysRequired() {
        const string dsl = """
            domain InvokeGrid
            Service: entity { Ping: action { } }
            Caller: entity {
              service: Service optional
              Go: action { invoke service.Ping }
            }
            """;
        var scenario = ParityScenario.FromDsl(dsl, "UnlinkedInvoke");
        var outcomes = await scenario.AssertAgree(side => {
            side.Create("Caller");
            side.Invoke("Go");
        });

        var refused = outcomes[^1];
        await Assert.That(refused.Success).IsFalse();
        await Assert.That(refused.FailureKind).IsEqualTo("Required");
        await Assert.That(refused.Message).Contains("requires a linked 'service'");
    }

    static ParityOutcome Step(IReadOnlyList<ParityOutcome> outcomes, string step) =>
        outcomes.First(o => o.Step == step);

    static string Dump(IReadOnlyList<ParityOutcome> outcomes) => string.Join(" | ", outcomes.Select(o =>
        o.Step + "[" + string.Join(",", o.State.Select(kv => $"{kv.Key}={kv.Value}")) + "]" + (o.ExceptionType is null ? "" : "!" + o.ExceptionType + ":" + o.Message)));

    /// <summary>A remainder (#548) read through an unlinked hop is absent like every other arithmetic: the comparison
    /// answers the absent rule instead of dereferencing the missing advisor, on both hosts.</summary>
    [Test]
    public async Task Policy_RemainderThroughUnlinkedHop_AnswersTheAbsentRule_OnBothHosts() {
        const string dsl = """
            domain Rem
            Advisor: entity { Score: Number }
            Customer: entity {
              Name: Text
              advisor: Advisor optional
              ScoreOdd: policy { advisor Score % 2 == 1 }
            }
            """;
        var scenario = ParityScenario.FromDsl(dsl, "RemainderThroughHop");

        var unlinked = await scenario.AssertAgree(side => {
            side.Create("Customer", ("Name", "Ada"));
            side.EvaluatePolicy("ScoreOdd");
        });
        await AssertAnswers(unlinked, new() { ["ScoreOdd"] = false });

        var linked = await scenario.AssertAgree(side => {
            var advisor = side.Create("Advisor", ("Score", 3L));
            side.Create("Customer", ("Name", "Ada"), ("advisor", advisor));
            side.EvaluatePolicy("ScoreOdd");
        });
        await AssertAnswers(linked, new() { ["ScoreOdd"] = true });
    }

    static async Task AssertAnswers(IReadOnlyList<ParityOutcome> outcomes, Dictionary<string, bool> expected) {
        foreach (var (policy, answer) in expected) {
            var actual = outcomes.First(o => o.Step == $"policy {policy}");
            await Assert.That(actual.State[policy]).IsEqualTo(answer ? "True" : "False")
                .Because($"{policy} (simulate and printed agree at this index)");
        }
    }
}