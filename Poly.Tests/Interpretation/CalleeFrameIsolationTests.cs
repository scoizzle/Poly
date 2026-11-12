using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// L-03v (#683): a callee must never observe a value a caller left behind. #605 (a decimal
/// <c>match … else</c> yields no value) is owned by T-09; this row pins only that the broken read yields
/// nothing (null), never an object the caller allocated. Zeroing the callee's frame slots does not
/// change the result: the word is a decimal scalar read as a heap handle, not a stale frame word.
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

    /// <summary>#605 verbatim, plus the whole-number variants the issue names: the else value is stored, and
    /// the simulator agrees with the printed C#.</summary>
    [Test]
    [Arguments("2.0", "2")]
    [Arguments("2", "2")]
    [Arguments("15", "15")]
    public async Task DecimalMatch_ElseArm_StoresTheElseValue_OnBothHosts(string elseArm, string expected) {
        var dsl = $$"""
            domain P

            Inv: entity {
              Qty: Number default(0)
              Dec: Decimal default(0)
              A: stage {
                Price: action { assign Dec to match { Qty > 1 -> 1.5, else -> {{elseArm}} } }
              }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "DecimalMatchElse" + elseArm.Replace(".", "_"))
            .AssertAgree(side => {
                side.Create("Inv");
                side.Invoke("Price");
            });
        await Assert.That(outcomes[1].State["Dec"]).IsEqualTo(expected);
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
        await Assert.That(dec?.ToString()).IsNotEqualTo("caller");
    }
}