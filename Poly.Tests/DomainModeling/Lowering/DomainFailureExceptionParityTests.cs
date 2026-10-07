using Poly.DomainModeling.Runtime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// C1b: a domain rule that fails where no DomainResult can be returned (constructor,
/// stage entry, subscription handler) throws <see cref="DomainFailureException"/> in
/// printed C# and in the simulator. Inside an action the same rule returns the
/// action's Failure. Host fail-loud errors stay plain InvalidOperationExceptions.
/// </summary>
public class DomainFailureExceptionParityTests {
    [Test]
    public async Task Create_WhenEntryBreaksRange_BothSidesThrowDomainFailureException() {
        const string dsl = """
            domain Lab
            Widget: entity {
              Score: Number range(1, 10) default(5)
              Bump: Number default(100)
              Draft: stage {
                entry { assign Score to Bump }
              }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "C1bCreateEntry")
            .AssertAgree(side => side.Create("Widget"));
        await Assert.That(outcomes[0].ExceptionType).IsEqualTo(nameof(DomainFailureException));
        await Assert.That(outcomes[0].Message).IsEqualTo("'Score' must be <= 10.");
    }

    [Test]
    public async Task Invoke_WhenTransitionEntryBreaksRange_BothSidesReturnTheActionFailure() {
        const string dsl = """
            domain Lab
            Widget: entity {
              Score: Number range(1, 10) default(5)
              Bump: Number default(50)
              Draft: stage { Ship: action { transition to Shipped } }
              Shipped: stage {
                entry { assign Score to Bump }
              }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "C1bTransitionEntry")
            .AssertAgree(side => {
                side.Create("Widget");
                side.Invoke("Ship");
            });
        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].ExceptionType).IsNull();
        await Assert.That(outcomes[1].Message).IsEqualTo("'Score' must be <= 10.");
    }

    [Test]
    public async Task Invoke_WhenAutomaticTransitionsLoop_HostFailLoudIsNotADomainFailure() {
        const string dsl = """
            domain E6
            Z: entity {
              Ready: Boolean
              A: stage { Go: action { transition to B } }
              B: stage { entry { if (Ready is true) { transition to C } } }
              C: stage { entry { if (Ready is true) { transition to B } } }
            }
            """;
        var outcomes = await ParityScenario.FromDsl(dsl, "C1bHostFailLoud")
            .AssertAgree(side => {
                side.Create("Z", ("Ready", true));
                side.Invoke("Go");
            });
        await Assert.That(outcomes[1].ExceptionType).IsEqualTo(nameof(InvalidOperationException));
    }
}
