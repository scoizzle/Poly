using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// F293: when Create takes no parameters, the exporter printed the EF Core parameterless
/// constructor and the full constructor with the same empty signature (CS0111). Each row
/// compiles the printed C# and checks it agrees with simulate.
/// </summary>
public class ParameterlessCreateTests {
    [Test]
    public async Task StageOnlyEntity_CompilesAndAgrees() {
        const string dsl = """
            domain S1
            Paper: entity {
              A: stage { Advance: action { transition to B } }
              B: stage { }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "ParameterlessStageOnly")
            .AssertAgree(side => {
                side.Create("Paper");
                side.Invoke("Advance");
            });
        await Assert.That(outcomes[0].State["Stage"]).IsEqualTo("A");
        await Assert.That(outcomes[1].State["Stage"]).IsEqualTo("B");
    }

    // Tag is set by the first stage's entry, so it is not a Create parameter either.
    [Test]
    public async Task FirstStageEntryAssignsOnlyProperty_CompilesAndAgrees() {
        const string dsl = """
            domain S2
            Z: entity {
              Tag: Text
              A: stage { entry { assign Tag to "entered" } }
              B: stage { }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "ParameterlessEntryAssign")
            .AssertAgree(side => side.Create("Z"));
        await Assert.That(outcomes[0].State["Stage"]).IsEqualTo("A");
        await Assert.That(outcomes[0].State["Tag"]).IsEqualTo("entered");
    }
}
