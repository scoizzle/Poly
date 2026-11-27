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
    /// An assign from an unlinked hop stores absent in an optional target, on both hosts: the Text target is
    /// the #936 example (P-13b), and the optional <em>Number</em> target is the same absent value in a
    /// value-type slot, not the <c>0</c> a bare null constant lowers to.
    /// </summary>
    [Test]
    public async Task Assign_ToOptionalNumberTarget_UnlinkedOptionalHop_StoresAbsent() {
        var scenario = ParityScenario.FromDsl(AssignDsl, "UnlinkedAssignOptionalNumber");
        var outcomes = await scenario.AssertAgree(side => {
            side.Create("Customer", ("MustScore", 7L));
            side.Invoke("Snapshot");
        });

        await Assert.That(outcomes[^1].Success).IsTrue()
            .Because($"{outcomes[^1].FailureKind} {outcomes[^1].Message}");
        await Assert.That(outcomes[^1].State["Seen"]).IsNull();
    }

    /// <summary>
    /// An assign from an unlinked hop stores absent in an optional Text target, on both hosts (#936, P-13b).
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

    /// <summary>
    /// A negated read through an unlinked hop reaches the constrained-assign wrap as absent, not as a value: the
    /// range-constrained Number target fails <c>Required</c> and keeps its value, on both hosts. A linked hop stores
    /// the negation, so the same wrap's range check runs on a real value.
    /// </summary>
    [Test]
    public async Task Assign_NegatedUnlinkedHop_ToRangeConstrainedNumber_FailsRequired_AndStoresTheNegationWhenLinked() {
        const string dsl = """
            domain NegRange
            Advisor: entity { Score: Number }
            Customer: entity {
              Name: Text
              advisor: Advisor optional
              Limited: Number range(-100, 100)
              Take: action { assign Limited to -(advisor Score) }
            }
            """;
        var scenario = ParityScenario.FromDsl(dsl, "UnlinkedNegatedRangeAssign");

        var unlinked = await scenario.AssertAgree(side => {
            side.Create("Customer", ("Name", "Ada"), ("Limited", 7L));
            side.Invoke("Take");
        });
        var refused = unlinked[^1];
        await Assert.That(refused.Success).IsFalse();
        await Assert.That(refused.FailureKind).IsEqualTo("Required");
        await Assert.That(refused.FailurePath).IsEqualTo("Customer.Limited");
        await Assert.That(refused.State["Limited"]).IsEqualTo("7");

        var linked = await scenario.AssertAgree(side => {
            var advisor = side.Create("Advisor", ("Score", 5L));
            side.Create("Customer", ("Name", "Ada"), ("Limited", 7L), ("advisor", advisor));
            side.Invoke("Take");
        });
        await Assert.That(linked[^1].Success).IsTrue()
            .Because($"{linked[^1].FailureKind} {linked[^1].Message}");
        await Assert.That(linked[^1].State["Limited"]).IsEqualTo("-5");
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

    /// <summary>A negated read through an unlinked hop is absent like every other arithmetic: the comparison
    /// answers the absent rule instead of dereferencing the missing advisor, on both hosts.</summary>
    [Test]
    public async Task Policy_NegationThroughUnlinkedHop_AnswersTheAbsentRule_OnBothHosts() {
        const string dsl = """
            domain Neg
            Advisor: entity { Score: Number }
            Customer: entity {
              Name: Text
              advisor: Advisor optional
              ScoreNeg: policy { 0 > -(advisor Score) }
            }
            """;
        var scenario = ParityScenario.FromDsl(dsl, "NegationThroughHop");

        var unlinked = await scenario.AssertAgree(side => {
            side.Create("Customer", ("Name", "Ada"));
            side.EvaluatePolicy("ScoreNeg");
        });
        await AssertAnswers(unlinked, new() { ["ScoreNeg"] = false });

        var linked = await scenario.AssertAgree(side => {
            var advisor = side.Create("Advisor", ("Score", 1L));
            side.Create("Customer", ("Name", "Ada"), ("advisor", advisor));
            side.EvaluatePolicy("ScoreNeg");
        });
        await AssertAnswers(linked, new() { ["ScoreNeg"] = true });
    }

    /// <summary>
    /// A <c>match</c> whose taken arm reads through an unlinked hop is absent, like the same read outside a
    /// match: an optional target stores absent and a required target fails <c>Required</c>, without ever
    /// dereferencing the missing advisor, on both hosts. An arm that does not read through the hop still
    /// yields its present value.
    /// </summary>
    [Test]
    public async Task Match_UnlinkedHopInArm_AnswersTheAbsentRule_OnBothHosts() {
        const string dsl = """
            domain MatchHop
            Advisor: entity { Score: Number }
            Customer: entity {
              MustScore: Number
              Seen: Number optional
              Flag: Boolean default(false)
              advisor: Advisor optional
              Force: action { assign MustScore to match { Flag -> advisor Score, else -> 1 } }
              Snapshot: action { assign Seen to match { Flag -> advisor Score, else -> 1 } }
            }
            """;
        var scenario = ParityScenario.FromDsl(dsl, "MatchUnlinkedHop");

        // The taken arm reads through the unlinked hop: the optional target stores absent ...
        var optional = await scenario.AssertAgree(side => {
            side.Create("Customer", ("MustScore", 7L), ("Flag", true));
            side.Invoke("Snapshot");
        });
        await Assert.That(optional[^1].Success).IsTrue()
            .Because($"{optional[^1].FailureKind} {optional[^1].Message}");
        await Assert.That(optional[^1].State["Seen"]).IsNull();

        // ... and the required target fails Required, leaving it as it was.
        var required = await scenario.AssertAgree(side => {
            side.Create("Customer", ("MustScore", 7L), ("Flag", true));
            side.Invoke("Force");
        });
        var refused = required[^1];
        await Assert.That(refused.Success).IsFalse();
        await Assert.That(refused.FailureKind).IsEqualTo("Required");
        await Assert.That(refused.FailurePath).IsEqualTo("Customer.MustScore");
        await Assert.That(refused.State["MustScore"]).IsEqualTo("7");

        // The else arm does not touch the hop, so the match is present even while it is unlinked.
        var otherwise = await scenario.AssertAgree(side => {
            side.Create("Customer", ("MustScore", 7L), ("Flag", false));
            side.Invoke("Snapshot");
        });
        await Assert.That(otherwise[^1].Success).IsTrue()
            .Because($"{otherwise[^1].FailureKind} {otherwise[^1].Message}");
        await Assert.That(otherwise[^1].State["Seen"]).IsEqualTo("1");

        // A linked advisor makes the taken arm present and yields its real value.
        var linked = await scenario.AssertAgree(side => {
            var advisor = side.Create("Advisor", ("Score", 5L));
            side.Create("Customer", ("MustScore", 7L), ("Flag", true), ("advisor", advisor));
            side.Invoke("Snapshot");
            side.Invoke("Force");
        });
        await Assert.That(linked[^1].Success).IsTrue()
            .Because($"{linked[^1].FailureKind} {linked[^1].Message}");
        await Assert.That(linked[^1].State["Seen"]).IsEqualTo("5");
        await Assert.That(linked[^1].State["MustScore"]).IsEqualTo("5");
    }

    const string QuantifierArmDsl = """
        domain QuantMatch
        Advisor: entity { Score: Number }
        Order: entity { Big: Boolean default(false) }
        Customer: entity {
          Seen: Number optional
          MustScore: Number
          advisor: Advisor optional
          orders: many Order
          Snapshot: action { assign Seen to match { any orders where Big -> advisor Score, else -> 1 } }
          Force: action { assign MustScore to match { any orders where Big -> advisor Score, else -> 1 } }
          BigScore: policy { match { any orders where Big -> advisor Score, else -> 1 } > 0 }
        }
        """;

    /// <summary>
    /// A quantifier arm condition hoists statements, and the arm it guards may read through an unlinked hop. The
    /// match chain evaluates the condition once and records which arm it took, so presence reads that record instead
    /// of rebuilding the condition: an untaken hop arm leaves the <c>else</c> value present, on both hosts.
    /// </summary>
    [Test]
    public async Task Match_QuantifierArmConditionWithHopArm_LowersAndAgrees_OnBothHosts() {
        var scenario = ParityScenario.FromDsl(QuantifierArmDsl, "MatchQuantHop");

        // No orders: the quantifier arm is not taken, so the else value is present even with an unlinked advisor.
        var unlinked = await scenario.AssertAgree(side => {
            side.Create("Customer", ("MustScore", 7L));
            side.Invoke("Snapshot");
        });
        await Assert.That(unlinked[^1].Success).IsTrue()
            .Because($"{unlinked[^1].FailureKind} {unlinked[^1].Message}");
        await Assert.That(unlinked[^1].State["Seen"]).IsEqualTo("1");

        // A linked advisor and no order: still the else value.
        var linked = await scenario.AssertAgree(side => {
            var advisor = side.Create("Advisor", ("Score", 4L));
            side.Create("Customer", ("MustScore", 7L), ("advisor", advisor));
            side.Invoke("Snapshot");
        });
        await Assert.That(linked[^1].Success).IsTrue()
            .Because($"{linked[^1].FailureKind} {linked[^1].Message}");
        await Assert.That(linked[^1].State["Seen"]).IsEqualTo("1");
    }

    /// <summary>The policy form of the same match: the absent rule answers the comparison on both hosts.</summary>
    [Test]
    public async Task Policy_QuantifierArmConditionWithHopArm_AnswersTheAbsentRule_OnBothHosts() {
        var scenario = ParityScenario.FromDsl(QuantifierArmDsl, "MatchQuantHopPolicy");

        var unlinked = await scenario.AssertAgree(side => {
            side.Create("Customer", ("MustScore", 7L));
            side.EvaluatePolicy("BigScore");
        });
        await Assert.That(unlinked[^1].State["BigScore"]).IsEqualTo("True");

        var linked = await scenario.AssertAgree(side => {
            var order = side.Create("Order", ("Big", true));
            var advisor = side.Create("Advisor", ("Score", 4L));
            side.Create("Customer", ("MustScore", 7L), ("advisor", advisor), ("orders", new[] { order }));
            side.EvaluatePolicy("BigScore");
        });
        await Assert.That(linked[^1].State["BigScore"]).IsEqualTo("True");
    }

    /// <summary>
    /// A taken arm that reads through an unlinked hop is absent: an optional target stores absent, a required target
    /// fails <c>Required</c>, and an ordering over it is false — no missing hop is dereferenced, on both hosts.
    /// </summary>
    [Test]
    public async Task Match_QuantifierArmCondition_TakenHopArmWithUnlinkedHop_IsAbsent_OnBothHosts() {
        var scenario = ParityScenario.FromDsl(QuantifierArmDsl, "MatchQuantHopTaken");

        var optional = await scenario.AssertAgree(side => {
            var order = side.Create("Order", ("Big", true));
            side.Create("Customer", ("MustScore", 7L), ("orders", new[] { order }));
            side.Invoke("Snapshot");
        });
        await Assert.That(optional[^1].Success).IsTrue()
            .Because($"{optional[^1].FailureKind} {optional[^1].Message}");
        await Assert.That(optional[^1].State["Seen"]).IsNull();

        var required = await scenario.AssertAgree(side => {
            var order = side.Create("Order", ("Big", true));
            side.Create("Customer", ("MustScore", 7L), ("orders", new[] { order }));
            side.Invoke("Force");
        });
        await Assert.That(required[^1].Success).IsFalse();
        await Assert.That(required[^1].FailureKind).IsEqualTo("Required");
        await Assert.That(required[^1].FailurePath).IsEqualTo("Customer.MustScore");
        await Assert.That(required[^1].State["MustScore"]).IsEqualTo("7");

        var policy = await scenario.AssertAgree(side => {
            var order = side.Create("Order", ("Big", true));
            side.Create("Customer", ("MustScore", 7L), ("orders", new[] { order }));
            side.EvaluatePolicy("BigScore");
        });
        await Assert.That(policy[^1].State["BigScore"]).IsEqualTo("False");
    }

    const string PinDsl = """
        domain Pin
        Advisor: entity { Score: Number Name: Text }
        Customer: entity {
          Flag: Boolean default(false)
          Seen: Number optional
          MustScore: Number
          Bounded: Number range(0, 10) default(0)
          Note: Text
          advisor: Advisor optional
          Nested: action { assign MustScore to match { Flag -> match { Flag -> advisor Score, else -> 2 }, else -> 1 } }
          NestedNull: action { assign Seen to match { Flag -> match { Flag -> null, else -> 3 }, else -> 1 } }
          BoundedScore: action { assign Bounded to match { Flag -> advisor Score, else -> 5 } }
          RequireNote: action { assign Note to match { Flag -> advisor Name, else -> "x" } }
          Fallback: action { assign MustScore to match { Flag -> 1, else -> Seen } }
          HopCondition: action { assign MustScore to match { advisor Score > 3 -> advisor Score, else -> 1 } }
        }
        """;

    /// <summary>
    /// A nested match as an arm value: the outer chain asks the inner match's recorded presence, so the selected
    /// inner arm that reads an unlinked hop makes the whole value absent — <c>Required</c> into a required target.
    /// </summary>
    [Test]
    public async Task Match_NestedMatchIntoRequiredTarget_UnlinkedHopFailsRequired_OnBothHosts() {
        var scenario = ParityScenario.FromDsl(PinDsl, "PinNestedMatch");

        var unlinked = await scenario.AssertAgree(side => {
            side.Create("Customer", ("Flag", true), ("MustScore", 7L), ("Note", "n"));
            side.Invoke("Nested");
        });
        await Assert.That(unlinked[^1].Success).IsFalse();
        await Assert.That(unlinked[^1].FailureKind).IsEqualTo("Required");
        await Assert.That(unlinked[^1].FailurePath).IsEqualTo("Customer.MustScore");

        var linked = await scenario.AssertAgree(side => {
            var advisor = side.Create("Advisor", ("Score", 4L), ("Name", "Pat"));
            side.Create("Customer", ("Flag", true), ("MustScore", 7L), ("Note", "n"), ("advisor", advisor));
            side.Invoke("Nested");
        });
        await Assert.That(linked[^1].Success).IsTrue()
            .Because($"{linked[^1].FailureKind} {linked[^1].Message}");
        await Assert.That(linked[^1].State["MustScore"]).IsEqualTo("4");
    }

    /// <summary>A nested match whose selected inner arm is <c>null</c> into an optional target stores absent.</summary>
    [Test]
    public async Task Match_NestedNullArmIntoOptionalTarget_StoresAbsent_OnBothHosts() {
        var scenario = ParityScenario.FromDsl(PinDsl, "PinNestedNull");
        var outcomes = await scenario.AssertAgree(side => {
            side.Create("Customer", ("Flag", true), ("MustScore", 7L), ("Note", "n"));
            side.Invoke("NestedNull");
        });
        await Assert.That(outcomes[^1].Success).IsTrue()
            .Because($"{outcomes[^1].FailureKind} {outcomes[^1].Message}");
        await Assert.That(outcomes[^1].State["Seen"]).IsNull();
    }

    /// <summary>A match into a range-constrained required target: unlinked is <c>Required</c>, a present out-of-range score is <c>Range</c>.</summary>
    [Test]
    public async Task Match_RangeConstrainedTarget_KeepsRequiredThenRange_OnBothHosts() {
        var scenario = ParityScenario.FromDsl(PinDsl, "PinRangeTarget");

        var unlinked = await scenario.AssertAgree(side => {
            side.Create("Customer", ("Flag", true), ("MustScore", 7L), ("Note", "n"));
            side.Invoke("BoundedScore");
        });
        await Assert.That(unlinked[^1].Success).IsFalse();
        await Assert.That(unlinked[^1].FailureKind).IsEqualTo("Required");
        await Assert.That(unlinked[^1].FailurePath).IsEqualTo("Customer.Bounded");

        var outOfRange = await scenario.AssertAgree(side => {
            var advisor = side.Create("Advisor", ("Score", 50L), ("Name", "Pat"));
            side.Create("Customer", ("Flag", true), ("MustScore", 7L), ("Note", "n"), ("advisor", advisor));
            side.Invoke("BoundedScore");
        });
        await Assert.That(outOfRange[^1].Success).IsFalse();
        await Assert.That(outOfRange[^1].FailureKind).IsEqualTo("Range");
        await Assert.That(outOfRange[^1].FailurePath).IsEqualTo("Customer.Bounded");
        await Assert.That(outOfRange[^1].State["Bounded"]).IsEqualTo("0");
    }

    /// <summary>A match into a required Text target: unlinked is <c>Required</c>, a linked name writes the value.</summary>
    [Test]
    public async Task Match_RequiredTextTarget_KeepsRequiredThenWrites_OnBothHosts() {
        var scenario = ParityScenario.FromDsl(PinDsl, "PinTextTarget");

        var unlinked = await scenario.AssertAgree(side => {
            side.Create("Customer", ("Flag", true), ("MustScore", 7L), ("Note", "n"));
            side.Invoke("RequireNote");
        });
        await Assert.That(unlinked[^1].Success).IsFalse();
        await Assert.That(unlinked[^1].FailureKind).IsEqualTo("Required");
        await Assert.That(unlinked[^1].FailurePath).IsEqualTo("Customer.Note");
        await Assert.That(unlinked[^1].State["Note"]).IsEqualTo("n");

        var linked = await scenario.AssertAgree(side => {
            var advisor = side.Create("Advisor", ("Score", 4L), ("Name", "Pat"));
            side.Create("Customer", ("Flag", true), ("MustScore", 7L), ("Note", "n"), ("advisor", advisor));
            side.Invoke("RequireNote");
        });
        await Assert.That(linked[^1].Success).IsTrue()
            .Because($"{linked[^1].FailureKind} {linked[^1].Message}");
        await Assert.That(linked[^1].State["Note"]).IsEqualTo("Pat");
    }

    /// <summary>An optional <c>else</c> arm that is absent into a required target fails <c>Required</c>, on both hosts.</summary>
    [Test]
    public async Task Match_OptionalElseArmIntoRequiredTarget_FailsRequired_OnBothHosts() {
        var scenario = ParityScenario.FromDsl(PinDsl, "PinOptionalElse");

        var absentElse = await scenario.AssertAgree(side => {
            side.Create("Customer", ("Flag", false), ("MustScore", 7L), ("Note", "n"));
            side.Invoke("Fallback");
        });
        await Assert.That(absentElse[^1].Success).IsFalse();
        await Assert.That(absentElse[^1].FailureKind).IsEqualTo("Required");
        await Assert.That(absentElse[^1].FailurePath).IsEqualTo("Customer.MustScore");
        await Assert.That(absentElse[^1].State["MustScore"]).IsEqualTo("7");

        var presentArm = await scenario.AssertAgree(side => {
            side.Create("Customer", ("Flag", true), ("MustScore", 7L), ("Note", "n"));
            side.Invoke("Fallback");
        });
        await Assert.That(presentArm[^1].Success).IsTrue()
            .Because($"{presentArm[^1].FailureKind} {presentArm[^1].Message}");
        await Assert.That(presentArm[^1].State["MustScore"]).IsEqualTo("1");
    }

    /// <summary>An arm condition that reads through the unlinked hop does not select its arm; a linked score does.</summary>
    [Test]
    public async Task Match_HopCondition_UnlinkedMissesTheArm_LinkedTakesIt_OnBothHosts() {
        var scenario = ParityScenario.FromDsl(PinDsl, "PinHopCondition");

        var unlinked = await scenario.AssertAgree(side => {
            side.Create("Customer", ("MustScore", 7L), ("Note", "n"));
            side.Invoke("HopCondition");
        });
        await Assert.That(unlinked[^1].Success).IsTrue()
            .Because($"{unlinked[^1].FailureKind} {unlinked[^1].Message}");
        await Assert.That(unlinked[^1].State["MustScore"]).IsEqualTo("1");

        var linked = await scenario.AssertAgree(side => {
            var advisor = side.Create("Advisor", ("Score", 4L), ("Name", "Pat"));
            side.Create("Customer", ("MustScore", 7L), ("Note", "n"), ("advisor", advisor));
            side.Invoke("HopCondition");
        });
        await Assert.That(linked[^1].Success).IsTrue()
            .Because($"{linked[^1].FailureKind} {linked[^1].Message}");
        await Assert.That(linked[^1].State["MustScore"]).IsEqualTo("4");
    }

    static async Task AssertAnswers(IReadOnlyList<ParityOutcome> outcomes, Dictionary<string, bool> expected) {
        foreach (var (policy, answer) in expected) {
            var actual = outcomes.First(o => o.Step == $"policy {policy}");
            await Assert.That(actual.State[policy]).IsEqualTo(answer ? "True" : "False")
                .Because($"{policy} (simulate and printed agree at this index)");
        }
    }
}