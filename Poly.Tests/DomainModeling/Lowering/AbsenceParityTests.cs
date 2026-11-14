using Poly.DomainModeling;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

using SN = Poly.Ast.Nodes;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-06 (#683): one target-type adapter owns every value-to-target-type decision in
/// lowering, so the shapes the DSL accepts simulate and print the same way. Each row
/// is a value the domain type-checks but lowering used to mistranslate, drop, or
/// leave untyped: an enum member in a create initializer (#485), an omitted enum
/// default (#494), an omitted scalar before a written slot (#495), bare and
/// string <c>match</c> arms assigned to an enum property (#498, #499), and an
/// integral default for a Decimal (#511).
/// </summary>
public class AbsenceParityTests {
    [Test]
    public async Task CreateIn_WhenEnumMemberInitializer_SimAndCSharpAgree() {
        var outcomes = await ParityScenario.FromDsl("""
            domain Ledger
            Kind: enum { Debit, Credit }
            Entry: entity {
              Kind: Kind
            }
            Acct: entity {
              Name: Text
              entries: many Entry
              Dep: action { create in entries { Kind: Credit } }
            }
            """, "ParityEnumMemberCreateIn")
            .AssertAgree(side => {
                side.Create("Acct", ("Name", "Ada"));
                side.Invoke("Dep");
                if (side.Outcomes[^1].Success)
                    side.UseLinked("entries");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["Kind"]).IsEqualTo("Credit");
    }

    [Test]
    public async Task CreateIn_WhenEnumDefaultOmitted_AppliesDefault() {
        var outcomes = await ParityScenario.FromDsl("""
            domain Ledger
            Kind: enum { A, B }
            Sighting: entity {
              V: Kind default(A)
              N: Number
            }
            Acct: entity {
              Name: Text
              seen: many Sighting
              Add: action { create in seen { N: 1 } }
            }
            """, "ParityEnumDefaultOmitted")
            .AssertAgree(side => {
                side.Create("Acct", ("Name", "Ada"));
                side.Invoke("Add");
                if (side.Outcomes[^1].Success)
                    side.UseLinked("seen");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["V"]).IsEqualTo("A");
    }

    [Test]
    public async Task CreateIn_WhenEnumFromParameterHoleBeforeWritten_Agrees() {
        // #495 verbatim: the hole (N) sorts before the written slot (V), so lowering
        // must pass a typed null and then the caller's enum value.
        var outcomes = await ParityScenario.FromDsl("""
            domain Ledger
            Kind: enum { A, B }
            Sighting: entity {
              V: Kind default(A)
              N: Text optional
            }
            Acct: entity {
              Name: Text
              seen: many Sighting
              Go: action (k: Kind) { create in seen { V: k } }
            }
            """, "ParityEnumHoleBeforeWritten")
            .AssertAgree(side => {
                side.Create("Acct", ("Name", "Ada"));
                side.Invoke("Go", ("k", "B"));
                if (side.Outcomes[^1].Success)
                    side.UseLinked("seen");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["V"]).IsEqualTo("B");
    }

    [Test]
    public async Task CreateIn_WhenHoleNameSortsAfterWritten_Agrees() {
        // Control for #495: renaming the hole so it sorts last makes it a trailing
        // omission, which lowering already drops. Passes before and after L-06.
        var outcomes = await ParityScenario.FromDsl("""
            domain Ledger
            Kind: enum { A, B }
            Sighting: entity {
              V: Kind default(A)
              ZNote: Text optional
            }
            Acct: entity {
              Name: Text
              seen: many Sighting
              Go: action (k: Kind) { create in seen { V: k } }
            }
            """, "ParityEnumHoleTrailing")
            .AssertAgree(side => {
                side.Create("Acct", ("Name", "Ada"));
                side.Invoke("Go", ("k", "B"));
                if (side.Outcomes[^1].Success)
                    side.UseLinked("seen");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["V"]).IsEqualTo("B");
    }

    [Test]
    public async Task CreateIn_WhenHoleBeforeDefaultedEnum_Agrees() {
        // The defaulted enum slot stops being a trailing omission (D3), so it arrives
        // beside the typed null the hole needs (D4). Neither may recreate the other's bug.
        var outcomes = await ParityScenario.FromDsl("""
            domain Ledger
            Kind: enum { A, B }
            Sighting: entity {
              N: Text optional
              V: Kind default(A)
            }
            Acct: entity {
              Name: Text
              seen: many Sighting
              Go: action { create in seen { } }
            }
            """, "ParityHoleBeforeDefaultedEnum")
            .AssertAgree(side => {
                side.Create("Acct", ("Name", "Ada"));
                side.Invoke("Go");
                if (side.Outcomes[^1].Success)
                    side.UseLinked("seen");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["V"]).IsEqualTo("A");
    }

    private static string ExpressionMatchDsl(string body) => $$"""
        domain Shop
        AbxInv: entity {
          Qty: Number default(3)
          Opt: Number optional
          T: Number default(0)
          TOpt: Number optional
          Flag: Boolean default(true)
          Run: action { {{body}} }
        }
        """;

    /// <summary>
    /// T-09a (left by T-06a): a <c>match</c> with a <c>null</c> or an optional arm used as an operand has no assignment
    /// target, and means the same on the simulator and the generated C# as the optional it is: an absent value compares
    /// and orders false, as an absent optional property does. Qty is 3; the observed member is read for Flag true, then false.
    /// </summary>
    [Test]
    [Arguments("if ((match { Flag -> null, else -> Qty }) == 3) { assign T to 1 }", "T", "0", "1")]
    [Arguments("if ((match { Flag -> null, else -> Qty }) != null) { assign T to 1 }", "T", "0", "1")]
    [Arguments("if ((match { Flag -> null, else -> Qty }) == null) { assign T to 1 }", "T", "1", "0")]
    [Arguments("if ((match { Flag -> null, else -> Qty }) < 5) { assign T to 1 }", "T", "0", "1")]
    [Arguments("if ((match { Flag -> null, else -> Qty }) > 1) { assign T to 1 }", "T", "0", "1")]
    [Arguments("if ((match { Flag -> Opt, else -> Qty }) > 1) { assign T to 1 }", "T", "0", "1")]
    [Arguments("assign TOpt to match { Flag -> Opt, else -> Qty }", "TOpt", "(absent)", "3")]
    [Arguments("assign TOpt to match { Flag -> Qty, else -> Opt }", "TOpt", "3", "(absent)")]
    public async Task MatchWithAnAbsentArm_InAnExpressionPosition_MeansTheSameOnBothHosts(
        string body, string observed, string whenTrue, string whenFalse) {
        var outcomes = await ParityScenario.FromDsl(ExpressionMatchDsl(body), "AbsenceExprMatch" + body.Aggregate(17, (h, c) => unchecked(h * 31 + c)).ToString("x"))
            .AssertAgree(side => {
                side.Create("AbxInv", ("Flag", true));
                side.Invoke("Run");
                side.Create("AbxInv", ("Flag", false));
                side.Invoke("Run");
            });
        await Assert.That(outcomes[1].Success).IsTrue().Because(outcomes[1].Message ?? "");
        await Assert.That(outcomes[1].State[observed]).IsEqualTo(whenTrue == "(absent)" ? null : whenTrue);
        await Assert.That(outcomes[3].Success).IsTrue().Because(outcomes[3].Message ?? "");
        await Assert.That(outcomes[3].State[observed]).IsEqualTo(whenFalse == "(absent)" ? null : whenFalse);
    }

    private static string ScenarioDsl(string members, string body, string extra = "") => $$"""
        domain Shop
        uses temporal
        AbxMood: enum { Happy, Sad }
        AbxBox: entity {
          W: Decimal default(0)
        }
        AbxOptBox: entity {
          W: Decimal optional
        }
        AbxScn: entity {
          Qty: Number default(3)
          Dec: Decimal default(2.5)
          Opt: Number optional
          Opt2: Number optional
          OptDec: Decimal optional
          OptTxt: Text optional
          OptBool: Boolean optional
          OptMood: AbxMood optional
          OptDate: Date optional
          OptDate2: Date optional
          Flag: Boolean default(true)
          Mood: AbxMood default(Happy)
          boxes: many AbxBox
          obox: many AbxOptBox
        {{members}}
          Run: action { {{body}} }
        }
        {{extra}}
        """;

    private static readonly (bool Flag, bool Present)[] Scenarios = [(true, false), (true, true), (false, false), (false, true)];

    /// <summary>One row, four scenarios in order: (Flag true, Opt absent), (true, present), (false, absent), (false, present).
    /// Present means Opt is 4 and Opt2 is 5. The observed member of each is joined with <c>|</c>, <c>-</c> for absent. Both hosts
    /// must agree, <c>polyc check</c> must accept, and the values are the ones written in the row, worked out by hand.</summary>
    private static async Task AssertScenarios(string id, string dsl, string observed, string expected, long[]? qtys = null) {
        var gate = GateParity.Check(dsl, SimStep.Create("AbxScn", """{"Flag":false,"Opt":4,"Opt2":5}""", "o"), SimStep.Invoke("o", "Run"));
        await Assert.That(gate.Analysis).IsTrue().Because(gate.ToString());
        await Assert.That(gate.Holds).IsTrue().Because(gate.ToString());
        var outcomes = await ParityScenario.FromDsl(dsl, "AbsenceScn_" + id).AssertAgree(side => {
            foreach (var qty in qtys ?? [3L])
                foreach (var (flag, present) in Scenarios) {
                    if (present)
                        side.Create("AbxScn", ("Flag", flag), ("Qty", qty), ("Opt", 4L), ("Opt2", 5L), ("OptTxt", "x"), ("OptBool", true),
                            ("OptMood", "Sad"), ("OptDate", new DateOnly(2026, 1, 2)), ("OptDate2", new DateOnly(2026, 1, 3)));
                    else side.Create("AbxScn", ("Flag", flag), ("Qty", qty));
                    side.Invoke("Run");
                }
        });
        var seen = outcomes.Where(o => o.Step.StartsWith("invoke", StringComparison.Ordinal))
            .Select(o => o.Success ? o.State[observed] ?? "-" : "FAIL " + o.Message);
        // A date reads back through the helper's own ToString (the machine's culture), so the expectation is rendered the same way
        // from the DateOnly values the scenario creates: {D1} is OptDate (2026-01-02), {D2} is OptDate2 (2026-01-03).
        expected = expected.Replace("{D1}", new DateOnly(2026, 1, 2).ToString(), StringComparison.Ordinal)
            .Replace("{D2}", new DateOnly(2026, 1, 3).ToString(), StringComparison.Ordinal);
        await Assert.That(string.Join("|", seen)).IsEqualTo(expected);
    }

    /// <summary>
    /// T-09a fix round (F1): an enum member beside a Text string literal unifies where the slot is the enum: a comparison operand
    /// against an enum property (either side, <c>==</c> and <c>!=</c>) and a match nested in a match assigned to the enum. The base
    /// lowered both with the enum as target and ran them on the simulator, the generated C# and <c>polyc</c>.
    /// </summary>
    [Test]
    [Arguments("F1a", "T: Number default(0)", "if (Mood == (match { Flag -> Happy, else -> \"Sad\" })) { assign T to 1 }", "T", "1|1|0|0")]
    [Arguments("F1b", "T: Number default(0)", "if ((match { Flag -> Happy, else -> \"Sad\" }) == Mood) { assign T to 1 }", "T", "1|1|0|0")]
    [Arguments("F1c", "T: Number default(0)", "if (Mood != (match { Flag -> Happy, else -> \"Sad\" })) { assign T to 1 }", "T", "0|0|1|1")]
    [Arguments("F1d", "T: Number default(0)", "if (Mood == (match { Flag -> \"Happy\", else -> Sad })) { assign T to 1 }", "T", "1|1|0|0")]
    [Arguments("F1e", "T: Number default(0)",
        "assign Mood to match { Flag -> match { Flag -> Happy, else -> \"Sad\" }, else -> Happy }", "Mood", "Happy|Happy|Happy|Happy")]
    [Arguments("F1f", "T: Number default(0)",
        "assign Mood to match { Qty > 9 -> Happy, else -> match { Flag -> Happy, else -> \"Sad\" } }", "Mood", "Happy|Happy|Sad|Sad")]
    public async Task EnumAndTextLiteralMatch_AsAComparisonOperandOrNested_MeansTheSameOnEveryHost(
        string id, string members, string body, string observed, string expected) =>
        await AssertScenarios(id, ScenarioDsl("  " + members, body), observed, expected);

    /// <summary>
    /// T-09a fix round (F2, F3): a match with an optional or <c>null</c> arm that needs a number converted to Decimal, or that is an
    /// operand of arithmetic, is typed and lowered through an optional local; the base ran the shapes with a <c>null</c> arm or
    /// with every arm optional, failed the others on the VM, and the head runs all of them on every host.
    /// </summary>
    [Test]
    [Arguments("F2a", "T: Decimal optional", "assign T to match { Qty > 9 -> null, Qty > 2 -> Opt, else -> 1.5 }", "-|4|-|4")]
    [Arguments("F2b", "T: Decimal optional", "assign T to match { Flag -> Opt, else -> Qty }", "-|4|3|3")]
    [Arguments("F2c", "T: Decimal optional", "assign T to match { Flag -> Opt, else -> 1.5 }", "-|4|1.5|1.5")]
    [Arguments("F2d", "T: Decimal optional", "assign T to match { Flag -> Opt, else -> Dec }", "-|4|2.5|2.5")]
    [Arguments("F2e", "T: Decimal optional", "assign T to match { Flag -> OptDec, else -> Qty }", "-|-|3|3")]
    [Arguments("F2f", "T: Decimal optional", "assign T to match { Flag -> Qty, else -> Opt }", "3|3|-|4")]
    [Arguments("F3a", "T: Number optional", "assign T to (match { Flag -> Opt, else -> Opt2 }) + 1", "-|5|-|6")]
    [Arguments("F3b", "T: Number optional", "assign T to (match { Flag -> null, else -> Opt }) + 1", "-|-|-|5")]
    [Arguments("F3c", "T: Number optional", "assign T to Qty + (match { Flag -> Opt, else -> Qty })", "-|7|6|6")]
    [Arguments("F3d", "T: Number optional", "assign T to (match { Flag -> Opt, else -> 1 }) + 1", "-|5|2|2")]
    [Arguments("F3e", "T: Number optional", "assign T to (match { Flag -> Opt, else -> Opt2 }) * 2", "-|8|-|10")]
    public async Task MatchWithAnOptionalArm_ConvertedOrUnderArithmetic_MeansTheSameOnEveryHost(
        string id, string members, string body, string expected) =>
        await AssertScenarios(id, ScenarioDsl("  " + members, body), "T", expected);

    /// <summary>
    /// T-09a fix round 2 (F4, a nested match): an absent-able match nested in another match is filled inside the outer arm's
    /// branch, so an inner arm that cannot be evaluated (<c>10 / Qty</c> with Qty 0) is not evaluated when the outer arm is not
    /// taken, as the conditional chain did on the base. Eight scenarios: Qty 0 then 2, each with (Flag true, Opt absent),
    /// (true, present), (false, absent), (false, present).
    /// </summary>
    [Test]
    [Arguments("H1", "T: Number optional", "assign T to match { Qty == 0 -> 0, else -> match { Flag -> Opt, else -> 10 / Qty } }", "", "T", "0|0|0|0|-|4|5|5")]
    [Arguments("H2", "T: Number optional", "assign T to match { Qty == 0 -> 0, else -> match { Flag -> null, else -> 10 / Qty } }", "", "T", "0|0|0|0|-|-|5|5")]
    [Arguments("H3", "T: Number default(0)",
        "if ((match { Qty == 0 -> 0, else -> match { Flag -> Opt, else -> 10 / Qty } }) > 4) { assign T to 1 }", "", "T", "0|0|0|0|0|0|1|1")]
    [Arguments("H4", "T: Number optional", "assign T to match { Qty == 0 -> 0, else -> match { Flag -> Opt, else -> AbxDiv(10, Qty) } }",
        "function AbxDiv(a: Number, b: Number): Number = a / b", "T", "0|0|0|0|-|4|5|5")]
    [Arguments("H5", "T: Number optional", "assign T to match { Qty == 0 -> 0, Flag -> Opt, else -> 10 / Qty }", "", "T", "0|0|0|0|-|4|5|5")]
    public async Task NestedAbsentableMatch_IsEvaluatedOnlyWhenItsArmIsTaken_OnEveryHost(
        string id, string members, string body, string extra, string observed, string expected) =>
        await AssertScenarios(id, ScenarioDsl("  " + members, body, extra), observed, expected, [0L, 2L]);

    /// <summary>
    /// T-09a fix round 2 (F3): optional arms of every kind in a match, in a target position and in a no-target position, and the
    /// shapes the optional local newly serves (a nested optional match in a Decimal match, a comparison with a Decimal, arithmetic
    /// then a comparison, a <c>create in</c> initializer). Opt is absent then 4; Opt2 absent then 5; Dec 2.5.
    /// </summary>
    [Test]
    [Arguments("I1", "T: Decimal optional", "assign T to match { Qty > 9 -> 1.5, else -> match { Flag -> Opt, else -> Dec } }", "T", "-|4|2.5|2.5")]
    [Arguments("I2", "T: Number default(0)", "if ((match { Flag -> Opt, else -> 1.5 }) > Dec) { assign T to 1 }", "T", "0|1|0|0")]
    [Arguments("I3", "T: Number default(0)", "if (((match { Flag -> Opt, else -> Opt2 }) + 1) > 3) { assign T to 1 }", "T", "0|1|0|1")]
    [Arguments("I5", "T: Number default(0)", "if ((match { Flag -> OptTxt, else -> \"dflt\" }) == \"x\") { assign T to 1 }", "T", "0|1|0|0")]
    [Arguments("I6", "T: Boolean optional", "assign T to match { Flag -> OptBool, else -> false }", "T", "-|True|False|False")]
    [Arguments("I7", "T: Number default(0)", "if ((match { Flag -> OptBool, else -> false }) == true) { assign T to 1 }", "T", "0|1|0|0")]
    [Arguments("I8", "T: Date optional", "assign T to match { Flag -> OptDate, else -> OptDate2 }", "T", "-|{D1}|-|{D2}")]
    [Arguments("I9", "T: Number default(0)", "if ((match { Flag -> OptDate, else -> OptDate2 }) != null) { assign T to 1 }", "T", "0|1|0|1")]
    [Arguments("I10", "T: AbxMood optional", "assign T to match { Flag -> OptMood, else -> Happy }", "T", "-|Sad|Happy|Happy")]
    [Arguments("I11", "T: Number default(0)", "if ((match { Flag -> OptMood, else -> Mood }) != null) { assign T to 1 }", "T", "0|1|1|1")]
    [Arguments("I12", "T: Number default(0)", "create in obox { W: match { Flag -> Opt, else -> 1.5 } }\n  assign T to count obox where W == 4", "T", "0|1|0|0")]
    public async Task OptionalArmOfEveryKind_MeansTheSameOnEveryHost(string id, string members, string body, string observed, string expected) =>
        await AssertScenarios(id, ScenarioDsl("  " + members, body), observed, expected);

    /// <summary>
    /// T-09a fix round (F5): a match whose arms are all Number into a Decimal slot is no longer adapted arm by arm (the deleted
    /// <c>Adapt</c>); the slot converts the value, at an assign, an invoke argument, a function argument and a <c>create in</c>
    /// initializer (read back through <c>sum</c>, so the stored value is observed).
    /// </summary>
    [Test]
    [Arguments("F5a", "T: Decimal default(0)", "assign T to match { Flag -> Qty, else -> 2 }", "", "3|3|2|2")]
    [Arguments("F5b", "T: Decimal default(0)\n  Take: action (x: Decimal) { assign T to x }", "invoke Take(x: match { Flag -> Qty, else -> 2 })", "", "3|3|2|2")]
    [Arguments("F5c", "T: Decimal default(0)", "assign T to AbxId(match { Flag -> Qty, else -> 2 })", "function AbxId(v: Decimal): Decimal = v", "3|3|2|2")]
    [Arguments("F5d", "T: Decimal default(0)", "create in boxes { W: match { Flag -> Qty, else -> 2 } }\n  assign T to sum boxes W", "", "3|3|2|2")]
    [Arguments("F5e", "T: Decimal default(0)", "assign T to match { Flag -> 2, else -> 3 }", "", "2|2|3|3")]
    public async Task NumberOnlyMatch_IntoADecimalSlot_StoresTheSameValueOnEveryHost(
        string id, string members, string body, string extra, string expected) =>
        await AssertScenarios(id, ScenarioDsl("  " + members, body, extra), "T", expected);

    [Test]
    public async Task Assign_WhenBareEnumMemberMatchArm_Agrees() {
        var outcomes = await ParityScenario.FromDsl("""
            domain Shop
            Tier: enum { Gold, Std }
            Order: entity {
              Total: Number
              Tier: Tier default(Std)
              Classify: action { assign Tier to match { Total > 5 -> Gold, else -> Std } }
            }
            """, "ParityMatchBareEnumArm")
            .AssertAgree(side => {
                side.Create("Order", ("Total", 10L));
                side.Invoke("Classify");
                side.Create("Order", ("Total", 1L));
                side.Invoke("Classify");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[1].State["Tier"]).IsEqualTo("Gold");
        await Assert.That(outcomes[3].Success).IsTrue()
            .Because($"step failed: {outcomes[3].ExceptionType} {outcomes[3].Message}");
        await Assert.That(outcomes[3].State["Tier"]).IsEqualTo("Std");
    }

    [Test]
    public async Task Assign_WhenTextMatchArmsToEnumProperty_CSharpCompiles() {
        var outcomes = await ParityScenario.FromDsl("""
            domain Shop
            Tier: enum { Gold, Standard }
            Order: entity {
              Total: Number
              Tier: Tier default(Standard)
              Classify: action { assign Tier to match { Total > 5 -> "Gold", else -> "Standard" } }
            }
            """, "ParityMatchTextEnumArm")
            .AssertAgree(side => {
                side.Create("Order", ("Total", 10L));
                side.Invoke("Classify");
                side.Create("Order", ("Total", 1L));
                side.Invoke("Classify");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[1].State["Tier"]).IsEqualTo("Gold");
        await Assert.That(outcomes[3].Success).IsTrue()
            .Because($"step failed: {outcomes[3].ExceptionType} {outcomes[3].Message}");
        await Assert.That(outcomes[3].State["Tier"]).IsEqualTo("Standard");
    }

    [Test]
    public async Task Assign_WhenParameterNamedLikeEnumMember_UsesParameter() {
        // Guard: an action parameter shadows an enum member of the same name.
        var outcomes = await ParityScenario.FromDsl("""
            domain Shop
            PatronStatus: enum { Active, Suspended }
            Patron: entity {
              Status: PatronStatus default(Active)
              Set: action (Suspended: PatronStatus) { assign Status to Suspended }
            }
            """, "ParityParamShadowsEnumMember")
            .AssertAgree(side => {
                side.Create("Patron");
                side.Invoke("Set", ("Suspended", "Active"));
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[1].State["Status"]).IsEqualTo("Active");
    }

    [Test]
    public async Task Assign_WhenOtherActionParameterNamedLikeEnumMember_StillQualifiesMember() {
        // B1: the shadow guard must be scoped to the action being lowered. A bare enum
        // member named like a parameter of ANOTHER action still qualifies.
        var outcomes = await ParityScenario.FromDsl("""
            domain Shop
            PatronStatus: enum { Active, Suspended }
            Patron: entity {
              Status: PatronStatus default(Active)
              Note: Text optional
              Hold: action (Suspended: Text) { assign Note to Suspended }
              Suspend: action { assign Status to Suspended }
            }
            """, "ParityOtherActionParameterShadows")
            .AssertAgree(side => {
                side.Create("Patron");
                side.Invoke("Suspend");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[1].State["Status"]).IsEqualTo("Suspended");
    }

    [Test]
    public async Task CreateIn_WhenBareNameIsAlsoAProperty_StaysInstanceRead() {
        // Guard: a bare initializer value that is a property of the creating entity
        // reads that property, it is not a member of the target enum.
        var outcomes = await ParityScenario.FromDsl("""
            domain Shop
            PatronStatus: enum { Active, Suspended }
            Patron: entity {
              Active: PatronStatus default(Active)
              Status: PatronStatus default(Active)
              kids: many Kid
              Add: action { create in kids { Status: Active } }
            }
            Kid: entity {
              Status: PatronStatus default(Active)
            }
            """, "ParityBareNameIsProperty")
            .AssertAgree(side => {
                side.Create("Patron", ("Active", "Suspended"), ("Status", "Active"));
                side.Invoke("Add");
                if (side.Outcomes[^1].Success)
                    side.UseLinked("kids");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["Status"]).IsEqualTo("Suspended");
    }

    [Test]
    public async Task CreateIn_WhenDecimalRangeDefaultZeroOmitted_Succeeds() {
        var outcomes = await ParityScenario.FromDsl("""
            domain Payroll
            Employee: entity {
              Name: Text
              Salary: Decimal range(0, 1000000) default(0)
            }
            Dept: entity {
              Name: Text
              staff: many Employee
              Hire: action { create in staff { Name: "Ada" } }
            }
            """, "ParityDecimalDefaultOmitted")
            .AssertAgree(side => {
                side.Create("Dept", ("Name", "R&D"));
                side.Invoke("Hire");
                if (side.Outcomes[^1].Success)
                    side.UseLinked("staff");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["Name"]).IsEqualTo("Ada");
    }

    [Test]
    public async Task CreateIn_WhenNumberAndEnumDefaultsTrail_Agrees() {
        // E2: the warehouse RegisterTruck shape — the factory call now carries the
        // trailing Number and enum defaults explicitly, and both sides link a child
        // whose stored values are the member name and the number.
        var outcomes = await ParityScenario.FromDsl("""
            domain Depot
            TruckStatus: enum { Idle, EnRoute }
            Truck: entity {
              Vin: Text length(17, 17)
              MaxLoad: Number range(1, 40000)
              CurrentLoad: Number range(0, 40000) default(0)
              Status: TruckStatus default(Idle)
            }
            Warehouse: entity {
              Name: Text
              trucks: many Truck
              RegisterTruck: action (vin: Text, maxLoad: Number) {
                create in trucks { Vin: vin MaxLoad: maxLoad }
              }
            }
            """, "ParityTrailingDefaultsCreateIn")
            .AssertAgree(side => {
                side.Create("Warehouse", ("Name", "W"));
                side.Invoke("RegisterTruck", ("vin", "1HGCM82633A004352"), ("maxLoad", 10L));
                if (side.Outcomes[^1].Success)
                    side.UseLinked("trucks");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["Status"]).IsEqualTo("Idle");
        await Assert.That(outcomes[2].State["CurrentLoad"]).IsEqualTo("0");
    }

    private const string OptionalScalarHolesDsl = """
        domain Ledger
        Tally: entity {
          N: Number optional
          D: Decimal optional
          V: Text optional
        }
        Acct: entity {
          Name: Text
          tallies: many Tally
          Add: action { create in tallies { V: "x" } }
        }
        """;

    [Test]
    public async Task CreateIn_WhenOptionalScalarHolesBeforeWrittenSlot_Agree() {
        // #495 for value types. V is optional too, so the slots are
        // [D hole, N hole, V written] and both typed nulls are KEPT in the call —
        // with a required V, V would be ctor slot 0 and D/N would be a trailing run
        // that is dropped, which exercises nothing.
        var outcomes = await ParityScenario.FromDsl(OptionalScalarHolesDsl, "ParityOptionalScalarHoles")
            .AssertAgree(side => {
                side.Create("Acct", ("Name", "Ada"));
                side.Invoke("Add");
                if (side.Outcomes[^1].Success)
                    side.UseLinked("tallies");
            });

        // The printed call is the proof the holes survived: two typed nulls, then the
        // written value. A dropped trailing run would carry only the value.
        var (domain, analysis) = EvolvedDomain.FromDsl(OptionalScalarHolesDsl);
        var printed = new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis));
        var add = ExportedCSharp.ExtractMethod(printed, "internal DomainResult Add(");
        await Assert.That(add).Contains("(decimal?)null");
        await Assert.That(add).Contains("(long?)null");

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["V"]).IsEqualTo("x");
        await Assert.That(outcomes[2].State["N"]).IsNull();
        await Assert.That(outcomes[2].State["D"]).IsNull();
    }

    [Test]
    public async Task CreateIn_WhenDecimalInitializerZero_Agrees() {
        // E8 green control: dropping the initializer Decimal arm leaves a written 0 as
        // 0L, which the VM and the printed Decimal both store as zero.
        var outcomes = await ParityScenario.FromDsl("""
            domain Payroll
            Employee: entity {
              Name: Text
              Salary: Decimal range(0, 1000000)
            }
            Dept: entity {
              Name: Text
              staff: many Employee
              Hire: action { create in staff { Name: "Ada" Salary: 0 } }
            }
            """, "ParityDecimalInitializerZero")
            .AssertAgree(side => {
                side.Create("Dept", ("Name", "R&D"));
                side.Invoke("Hire");
                if (side.Outcomes[^1].Success)
                    side.UseLinked("staff");
            });

        await Assert.That(outcomes[1].Success).IsTrue()
            .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
        await Assert.That(outcomes[2].State["Salary"]).IsEqualTo("0");
    }

    [Test]
    public async Task Adapt_WhenStringNamesEnumMember_QualifiesMember() {
        // The #683 Node-form contract: an already-lowered string constant naming a member.
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Shop
            Tier: enum { Gold, Std }
            Order: entity { Tier: Tier default(Std) }
            """);
        var context = new LoweringContext(new SN.ThisReference(), analysis, domain, ActionResultType: new NamedTypeReference("DomainResult"));
        var target = domain.Types.OfType<Entity>().Single(e => e.Name == "Order")
            .Properties.Single(p => p.Name == "Tier").Type;

        var adapted = TargetTypeAdapter.Adapt(new SN.Constant("Gold"), target, context);

        await Assert.That(adapted).IsTypeOf<SN.Member>();
        await Assert.That(((SN.Member)adapted).MemberName).IsEqualTo("Gold");
        await Assert.That(((SN.NamedTypeReference)((SN.Member)adapted).Value).TypeName)
            .IsEqualTo("Tier");
    }

    [Test]
    public async Task Adapt_WhenIntegralConstantForDecimalTarget_MakesDecimal() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Pay
            Employee: entity { Salary: Decimal range(0, 1000000) }
            """);
        var context = new LoweringContext(new SN.ThisReference(), analysis, domain, ActionResultType: new NamedTypeReference("DomainResult"));
        var target = domain.Types.OfType<Entity>().Single(e => e.Name == "Employee")
            .Properties.Single(p => p.Name == "Salary").Type;

        var adapted = TargetTypeAdapter.Adapt(new SN.Constant(0L), target, context);

        await Assert.That(adapted).IsTypeOf<SN.Constant>();
        await Assert.That(((SN.Constant)adapted).Value).IsEqualTo(0m);
    }

    /// <summary>
    /// Pin: a function body names only its parameters (DMFUN005), so the nested pass
    /// keeps an empty enum-property map. An empty map means "no enum properties are
    /// known here" — a lookup miss, not a second mode — and the comparison stays text
    /// (<c>Calls.cs</c>). L-23 owns the C#-typed function body and flips this row when
    /// it lands; until then it keeps the current shape from drifting as the adapter
    /// takes over comparisons.
    /// </summary>
    [Test]
    public async Task FunctionBody_WhenEnumComparison_StillLowersAsText() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Shop
            PatronStatus: enum { Active, Suspended }
            function IsActive(s: PatronStatus): Boolean = s is "Active"
            Patron: entity {
              Status: PatronStatus default(Active)
              ActiveNow: policy { IsActive(Status) }
            }
            """);

        var printed = new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis));

        // The function body compares the bound argument to the text literal: no
        // enum type is known inside the nested pass, so nothing is qualified.
        await Assert.That(printed).Contains("ActiveNow() => this.Status == \"Active\"");
        await Assert.That(printed).DoesNotContain("ActiveNow() => this.Status == PatronStatus.Active");
    }

    /// <summary>
    /// Pin: the module's enum member carries the enum type (D1), so a lowered enum
    /// argument resolves against the Create signature. The stored value stays the
    /// member name, which the VM and the printed enum both read back as text.
    /// </summary>
    [Test]
    public async Task ModuleEnumMember_CarriesEnumTypeAndMemberName() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Shop
            PatronStatus: enum { Active, Suspended }
            Patron: entity {
              Status: PatronStatus default(Active)
            }
            """);

        var types = DomainProgramProjection.ToSyntax(domain, analysis);
        var status = types.Single(t => t.Name == "PatronStatus");
        var member = status.Fields!.Single(f => f.Name == "Active");

        await Assert.That(member.FieldType).IsTypeOf<SN.NamedTypeReference>();
        await Assert.That(((SN.NamedTypeReference)member.FieldType).TypeName).IsEqualTo("PatronStatus");
        await Assert.That((member.DefaultValue as SN.Constant)?.Value).IsEqualTo("Active");
    }
}