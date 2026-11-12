using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Runtime;

/// <summary>
/// S-12 (#681, #585): the owned-target parity file. R1 pins create-in on an owned collection
/// to the same absolute state on both hosts. The second-owner rows (R2 to R6) belong to S-12b,
/// which edits this file once the Link op refuses a second owner (L-13).
/// </summary>
public class OwnedTargetSingleOwnerParityTests {
    [Test]
    public async Task CreateIn_OwnedCollection_AgreeWithAbsoluteState() {
        var scenario = ParityScenario.FromDsl("""
            domain Shop
            Customer: entity {
              Name: Text
              orders: many owned Order
              Place: action { create in orders { Total: 1 } }
            }
            Order: entity {
              Total: Number
            }
            """, "ParityOwnedCreateInCollection");

        var (simulate, printed) = scenario.Run(side => {
            side.Create("Customer", ("Name", "Ann"));
            side.Invoke("Place");
        });

        await Assert.That(ParityScenario.Differences(simulate, printed)).IsEmpty();
        foreach (var outcomes in new[] { simulate, printed }) {
            await Assert.That(outcomes[1].Success).IsTrue()
                .Because($"step failed: {outcomes[1].ExceptionType} {outcomes[1].Message}");
            await Assert.That(outcomes[1].State["orders"]).IsEqualTo("1");
        }
    }
}