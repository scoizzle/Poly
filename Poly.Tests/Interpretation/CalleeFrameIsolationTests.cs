using Poly.Runtime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// L-03v (#683, #605, #623): a whole-number arm of a <c>match</c> assigned to a Decimal slot is converted in
/// Lower, so the simulator and the printed C# store the same value; the VM rejects a conditional whose arms
/// differ in representation instead of reading a scalar word as a heap handle. A non-literal arm
/// (<c>else -> Qty</c>) and a <c>match</c> with no target (<c>match { … } * 2</c>) are not converted here: they
/// fail closed with a positioned rejection, and T-09 owns converting them (those rows then flip to
/// <c>AssertAgree</c>).
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

    /// <summary>A non-literal arm and a target-less match are not converted in this slice: the VM rejects the
    /// mixed-representation conditional with a positioned error, never a wrong stored value. T-09 converts them.</summary>
    [Test]
    [Arguments("match { Qty > 1 -> 1.5, else -> Qty }")]
    [Arguments("match { Qty > 1 -> 1.5, else -> 2 } * 2")]
    public async Task DecimalMatch_NonLiteralOrTargetlessArm_FailsClosed(string rhs) {
        var (domain, _) = EvolvedDomain.FromDsl(Stage(rhs));
        var exec = ModuleExec.For(domain);
        var created = exec.Create("Inv", new Dictionary<string, object?>());
        await Assert.That(created.Succeeded).IsTrue();

        string message;
        try {
            var invoked = exec.Invoke(created.Instance!, "Price");
            message = invoked.Succeeded ? "(succeeded)" : invoked.Error ?? "";
        }
        catch (InvalidOperationException ex) {
            message = ex.Message;
        }
        await Assert.That(message).Contains("VM compile rejected: conditional arms differ in representation");
    }
}