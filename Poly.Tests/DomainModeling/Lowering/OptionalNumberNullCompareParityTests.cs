using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// V-02 closing round, PROBE 2: the null rule in <c>EffectInvariantAnalyzer</c> (a leaf that holds for null narrows an
/// optional property's present values but never empties it) rests on how the two hosts compare a NULL optional Number:
/// a positive comparison is false for null, its negation is true, <c>null != 5</c> is true, and an unset optional
/// reads as null, not 0. Each case runs on the simulator and on the compiled printed C#, and both must agree.
/// </summary>
public class OptionalNumberNullCompareParityTests {
    private static string Dsl(string guard) => $$"""
        domain G
        E: entity {
          W: Number optional range(0, 10)
          X: Number range(0, 10)
          P: policy { {{guard}} }
          Go: action require P { assign X to 1 }
        }
        """;

    private const string BranchDsl = """
        domain G
        E: entity {
          W: Number optional range(0, 10)
          X: Number range(0, 10)
          Go: action {
            if (W <= 20) { assign X to 0 } else { assign X to 10 }
          }
        }
        """;

    private static async Task<(ParityOutcome Invoke, ParityOutcome Create)> Run(string dsl, string name, long? w) {
        var outcomes = await ParityScenario.FromDsl(dsl, name).AssertAgree(side => {
            if (w is { } present) side.Create("E", ("W", present), ("X", 3L));
            else side.Create("E", ("X", 3L));
            side.Invoke("Go");
        });
        return (outcomes[1], outcomes[0]);
    }

    [Test]
    [Arguments("not (W >= 0)", null, true, "1")]
    [Arguments("not (W >= 0)", 5L, false, "3")]
    [Arguments("W != 5", null, true, "1")]
    [Arguments("W != 5", 5L, false, "3")]
    [Arguments("W != 5", 7L, true, "1")]
    [Arguments("W >= 0", null, false, "3")]
    [Arguments("W >= 0", 5L, true, "1")]
    public async Task Guard_OnAnOptionalNumber_BothHostsAgreeWithTheNullRule(string guard, long? w, bool permitted, string x) {
        var (invoke, create) = await Run(Dsl(guard), "OptionalNullGuard" + Math.Abs((guard + w).GetHashCode()), w);
        await Assert.That(create.Success).IsTrue();
        await Assert.That(create.State["W"]).IsEqualTo(w?.ToString());
        await Assert.That(invoke.Success).IsEqualTo(permitted);
        await Assert.That(invoke.State["X"]).IsEqualTo(x);
        if (!permitted) await Assert.That(invoke.FailureKind).IsNotNull();
    }

    [Test]
    [Arguments(null, "10")]
    [Arguments(5L, "0")]
    public async Task IfOnAnOptionalNumber_NullTakesTheElseBranch_OnBothHosts(long? w, string x) {
        var (invoke, _) = await Run(BranchDsl, "OptionalNullBranch" + (w?.ToString() ?? "Unset"), w);
        await Assert.That(invoke.Success).IsTrue();
        await Assert.That(invoke.State["X"]).IsEqualTo(x);
    }
}