using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Runtime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// L-03v (#683, #605, #623) and T-09a: a <c>match</c> has one type, and Lower converts every arm to it with an
/// explicit cast, so the simulator and the printed C# store the same value for a whole-number arm, a non-literal
/// Number arm (<c>else -> Qty</c>) and a <c>match</c> with no target (<c>match { … } * 2</c>). The VM still rejects
/// a conditional whose arms differ in representation instead of reading a scalar word as a heap handle: that is the
/// backstop for an API-built tree, not the DSL's defence.
/// </summary>
public class CalleeFrameIsolationTests {
    private const string Dsl = """
        domain P

        Inv: entity {
          Qty: Number default(0)
          Tag: Text default("none")
          Dec: Decimal default(0)
          Price: action { assign Dec to match { Qty > 1 -> 1.5, else -> 2.0 } }
          Run: action {
            assign Tag to "caller"
            invoke Price
          }
        }
        """;

    private static string Stage(string match) => $$"""
        domain P

        Inv: entity {
          Qty: Number default(0)
          Rate: Decimal default(0)
          Dec: Decimal default(0)
          A: stage {
            Price: action { assign Dec to {{match}} }
          }
        }
        """;

    /// <summary>#605 verbatim, plus the whole-number variants the issue names: the else value is stored, and
    /// the simulator agrees with the printed C#.</summary>
    [Test]
    [Arguments("2.0", "2")]
    [Arguments("2", "2")]
    [Arguments("15", "15")]
    public async Task DecimalMatch_ElseArm_StoresTheElseValue_OnBothHosts(string elseArm, string expected) {
        var outcomes = await ParityScenario
            .FromDsl(Stage($"match {{ Qty > 1 -> 1.5, else -> {elseArm} }}"), "DecimalMatchElse" + elseArm.Replace(".", "_"))
            .AssertAgree(side => {
                side.Create("Inv");
                side.Invoke("Price");
            });
        await Assert.That(outcomes[1].State["Dec"]).IsEqualTo(expected);
    }

    /// <summary>The DSL has no negative literal in an arm position: a unary minus is a positioned grammar error, so
    /// there is no negative whole-number arm to convert (pinned so a later grammar change revisits this row).</summary>
    [Test]
    public async Task DecimalMatch_NegativeLiteralArm_IsAPositionedGrammarError() {
        string message;
        try {
            _ = EvolvedDomain.FromDsl(Stage("match { Qty > 1 -> 1.5, else -> -1 }"));
            message = "(parsed)";
        }
        catch (Exception ex) {
            message = ex.Message;
        }
        await Assert.That(message).Contains("Expected expression, got '-'").And.Contains("line");
    }

    /// <summary>The reversed order: the whole-number arm comes first and the Decimal arm is the else.</summary>
    [Test]
    [Arguments(0L, "1.5")]
    [Arguments(5L, "2")]
    public async Task DecimalMatch_WholeNumberArmFirst_StoresTheTakenArm_OnBothHosts(long qty, string expected) {
        var outcomes = await ParityScenario
            .FromDsl(Stage("match { Qty > 1 -> 2, else -> 1.5 }"), "DecimalMatchReversed" + qty)
            .AssertAgree(side => {
                side.Create("Inv", ("Qty", qty));
                side.Invoke("Price");
            });
        await Assert.That(outcomes[1].State["Dec"]).IsEqualTo(expected);
    }

    /// <summary>#623: an integer-literal else stored the condition's own operand (<c>Rate</c>) in the simulator.</summary>
    [Test]
    public async Task DecimalMatch_IntegerElse_DoesNotStoreTheConditionOperand_OnBothHosts() {
        var outcomes = await ParityScenario
            .FromDsl(Stage("match { Rate > 5 -> 1.5, else -> 2 }"), "DecimalMatchRate")
            .AssertAgree(side => {
                side.Create("Inv", ("Rate", 3.5m), ("Dec", 9m));
                side.Invoke("Price");
            });
        await Assert.That(outcomes[1].State["Dec"]).IsEqualTo("2");
    }

    /// <summary>The same VM-level check L-03b needs: the action stores the else value and no scope token or
    /// other object reaches the member.</summary>
    [Test]
    public async Task DecimalMatchWithElse_StoresTheElseValue_NeverAScopeToken() {
        var (domain, _) = EvolvedDomain.FromDsl(Stage("match { Qty > 1 -> 1.5, else -> 2.0 }"));
        var exec = ModuleExec.For(domain);
        var created = exec.Create("Inv", new Dictionary<string, object?>());
        await Assert.That(created.Succeeded).IsTrue();
        var invoked = exec.Invoke(created.Instance!, "Price");
        await Assert.That(invoked.Succeeded).IsTrue();
        await Assert.That(created.Instance!.Bag["Dec"] is ActionScopeToken).IsFalse();
        await Assert.That(created.Instance!.Bag["Dec"]).IsEqualTo(2m);
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
    }

    [Test]
    public async Task CalleeDecimalMatch_NeverYieldsACallerHeapValue() {
        var (domain, _) = EvolvedDomain.FromDsl(Dsl);
        var exec = ModuleExec.For(domain);
        var created = exec.Create("Inv", new Dictionary<string, object?>());
        await Assert.That(created.Succeeded).IsTrue();
        _ = exec.Invoke(created.Instance!, "Run");

        var dec = created.Instance!.Bag["Dec"];
        await Assert.That(dec is string).IsFalse();
        await Assert.That(dec).IsEqualTo(2m);
    }

    /// <summary>T-09a: a non-literal Number arm and a target-less match are converted to Decimal in Lower: the simulator
    /// and the printed C# store the same value, taken from the arm the condition picks.</summary>
    [Test]
    [Arguments("match { Qty > 1 -> 1.5, else -> Qty }", 5L, "1.5")]
    [Arguments("match { Qty > 1 -> 1.5, else -> Qty }", 0L, "0")]
    [Arguments("match { Qty > 1 -> 1.5, else -> Qty }", 1L, "1")]
    [Arguments("match { Qty > 1 -> 1.5, else -> 2 } * 2", 5L, "3.0")]
    [Arguments("match { Qty > 1 -> 1.5, else -> 2 } * 2", 0L, "4")]
    public async Task DecimalMatch_NonLiteralOrTargetlessArm_AgreesOnBothHosts(string rhs, long qty, string expected) {
        var outcomes = await ParityScenario
            .FromDsl(Stage(rhs), "DecimalMatchConverted" + Math.Abs(rhs.GetHashCode()) + qty)
            .AssertAgree(side => {
                side.Create("Inv", ("Qty", qty));
                side.Invoke("Price");
            });
        await Assert.That(outcomes[1].Success).IsTrue();
        await Assert.That(outcomes[1].State["Dec"]).IsEqualTo(expected);
    }

    /// <summary>T-09a: <c>Maybe</c> invokes <c>Price</c> only when <c>Qty is 5</c>. <c>Price</c>'s body now compiles (its
    /// Number arm is converted to Decimal), so the caller compiles and runs on both hosts, and an unrelated action
    /// still runs. L-03b made a caller of a body that cannot compile fail to compile; that is no longer reached.</summary>
    [Test]
    public async Task CallerOfADecimalNumberMatchAction_Runs() {
        var outcomes = await ParityScenario.FromDsl("""
            domain P

            Inv: entity {
              Qty: Number default(0)
              Dec: Decimal default(0)
              Price: action { assign Dec to match { Qty > 9 -> 1.5, else -> Qty } }
              Maybe: action { if (Qty is 5) { invoke Price } }
              Bump: action { assign Qty to Qty + 1 }
            }
            """, "CallerOfDecimalNumberMatch").AssertAgree(side => {
            side.Create("Inv", ("Qty", 5L));
            side.Invoke("Maybe");
            side.Invoke("Bump");
        });
        await Assert.That(outcomes[1].Success).IsTrue();
        await Assert.That(outcomes[1].State["Dec"]).IsEqualTo("5");
        await Assert.That(outcomes[2].State["Qty"]).IsEqualTo("6");
    }

    // ── T-09a: the position × host matrix ───────────────────────────────────

    private const string ArmsMixed = "match { Flag -> 1.5, else -> Qty }";

    /// <summary>One position a <c>match</c> with a Decimal and a non-literal Number arm is written in, and what the
    /// observed member reads for Flag true and Flag false (Qty is 3).</summary>
    public sealed record MatchPosition(
        string Name, string Members, string Body, string Extra, string Observed, string WhenTrue, string WhenFalse) {
        public override string ToString() => Name;
    }

    public static IEnumerable<MatchPosition> MatchPositions() {
        yield return new("assign", "", $"assign Dec to {ArmsMixed}", "", "Dec", "1.5", "3");
        yield return new("function argument", "", $"assign Dec to MxTwice({ArmsMixed})",
            "function MxTwice(v: Decimal): Decimal = v * 2", "Dec", "3.0", "6");
        yield return new("invoke argument", "", $"invoke Take(x: {ArmsMixed})", "", "Dec", "1.5", "3");
        yield return new("create initializer", "", $"create MxBox {{ W: {ArmsMixed} }}",
            "MxBox: entity {\n  W: Decimal default(0)\n}", "Dec", "0", "0");
        yield return new("policy body", $"Ok: policy {{ ({ArmsMixed}) > 2 }}", "assign Dec to 0", "", "Ok", "False", "True");
        yield return new("quantifier body", $"AnyBig: policy {{ any lines where ({ArmsMixed}) > 2 }}", "assign Dec to 0", "",
            "AnyBig", "False", "True");
        yield return new("default", "D2: Decimal default(match { true -> 1.5, else -> 2 })", "assign Dec to 0", "", "D2", "1.5", "1.5");
        yield return new("nested match", "", "assign Dec to match { Flag -> match { Qty > 1 -> 1.5, else -> Qty }, else -> 2 }", "",
            "Dec", "1.5", "2");
        yield return new("guard operand", "", $"if (({ArmsMixed}) > 2) {{ assign Dec to 9 }}", "", "Dec", "0", "9");
        yield return new("target-less operand", "", $"assign Dec to ({ArmsMixed}) * 2", "", "Dec", "3.0", "6");
    }

    private static string PositionDsl(MatchPosition position) => $$"""
        domain P

        MxLine: entity {
          Qty: Number default(3)
          Flag: Boolean default(true)
        }
        {{position.Extra}}
        MxInv: entity {
          Qty: Number default(3)
          Dec: Decimal default(0)
          Flag: Boolean default(true)
          {{position.Members}}
          lines: many MxLine
          Take: action (x: Decimal) { assign Dec to x }
          Run: action { {{position.Body}} }
        }
        """;

    /// <summary>
    /// T-09a, amendment 3: a <c>match</c> whose arms are Decimal and a non-literal Number means the same in every position
    /// it is written in, on the simulator, the generated C# and <c>polyc check</c>; the observed member is the arm
    /// the condition picks, converted to Decimal.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(MatchPositions))]
    public async Task DecimalNumberMatch_AtEveryPosition_MeansTheSameOnEveryHost(MatchPosition position) {
        var dsl = PositionDsl(position);
        var isPolicy = position.Observed is "Ok" or "AnyBig";
        var gate = GateParity.Check(dsl,
            [SimStep.Create("MxInv", """{"Flag":false}""", "o"), SimStep.Invoke("o", "Run"),
                .. isPolicy ? [SimStep.Evaluate("MxInv", position.Observed, "o")] : Array.Empty<SimStep>()]);
        await Assert.That(gate.Analysis).IsTrue().Because(gate.ToString());
        await Assert.That(gate.Holds).IsTrue().Because(gate.ToString());

        var block = isPolicy ? 4 : 3;
        var outcomes = await ParityScenario.FromDsl(dsl, "MatchPosition" + position.Name.Replace(' ', '_').Replace('-', '_'))
            .AssertAgree(side => {
                foreach (var flag in new[] { true, false }) {
                    var line = side.Create("MxLine", ("Qty", 3L), ("Flag", flag));
                    side.Create("MxInv", ("Flag", flag), ("lines", new[] { line }));
                    side.Invoke("Run");
                    if (isPolicy) side.EvaluatePolicy(position.Observed);
                }
            });
        await Assert.That(outcomes[block - 1].State[position.Observed]).IsEqualTo(position.WhenTrue);
        await Assert.That(outcomes[2 * block - 1].State[position.Observed]).IsEqualTo(position.WhenFalse);
    }

    /// <summary>A function body may not hold a <c>match</c>: the position is a positioned analysis error on every host, so a
    /// Decimal and Number match as a function's return is never reached by Lower.</summary>
    [Test]
    public async Task DecimalNumberMatch_AsAFunctionReturn_IsAPositionedAnalysisError() {
        var gate = GateParity.Check("""
            domain P
            function MxPick(f: Boolean, q: Number): Decimal = match { f -> 1.5, else -> q }
            MxFn: entity {
              Qty: Number default(3)
              Dec: Decimal default(0)
              Run: action { assign Dec to MxPick(true, Qty) }
            }
            """);
        await Assert.That(gate.Analysis).IsFalse().Because(gate.ToString());
        await Assert.That(gate.AnalysisSummary).Contains("MxPick").And.Contains("MatchExpr");
    }

    /// <summary>
    /// T-09a, amendment 3: Lower reads the match typing the analysis stamped and fails closed, positioned, when it is
    /// absent: a <c>match</c> that was never analysed does not lower to a conditional of unknown arm types.
    /// </summary>
    [Test]
    public async Task LowerMatch_WithoutTheTypingStamp_FailsClosed_AtTheMatch() {
        var match = new MatchExpr(
            [new MatchArm(DomainExpression.Literal(true), DomainExpression.Literal(1L))], DomainExpression.Literal(2L), Line: 7, Col: 12);
        var subject = new Poly.Ast.Nodes.Parameter("this");
        var pass = new DomainExpressionLoweringPass(LoweringFixtures.MinimalContext(subject));
        string message;
        try {
            _ = pass.LowerExpression(match, subject);
            message = "(lowered)";
        }
        catch (InvalidOperationException ex) {
            message = ex.Message;
        }
        await Assert.That(message).Contains("'match' has no analysed type").And.Contains("line 7, col 12");
    }
}