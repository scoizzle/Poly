namespace Poly.Tests.DomainModeling.Runtime;

/// <summary>
/// S-12 (#681, #585): the owned-target parity file. R1 pins create-in on an owned collection
/// to the same absolute state on both hosts. R2 is S-12b's second-owner refusal: an owned part
/// has one owner, so a link that would give it a second one is a <c>Link</c> failure on both
/// hosts. The create half of the rule is #1067's (the factory pairs a collection argument's
/// children with the child's owner navigation).
/// </summary>
public class OwnedTargetSingleOwnerParityTests {
    // An owned collection whose target names its owner (`customer` pairs with `orders`): the one
    // shape in which generated C# can see who already owns the part.
    const string Ordered = """
        domain Shop
        Customer: entity {
          Name: Text
          orders: many owned Order
        }
        Order: entity {
          Total: Number default(0)
          customer: Customer optional
        }
        """;

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

    [Test]
    public async Task SecondOwner_Rejected_InSimAndPrintedCSharp() {
        var scenario = ParityScenario.FromDsl(Ordered, "ParityOwnedSecondOwner");

        var (simulate, printed) = scenario.Run(side => {
            var ann = side.Create("Customer", ("Name", "Ann"));
            var order = side.Create("Order", ("customer", ann));
            var bob = side.Create("Customer", ("Name", "Bob"));
            side.Use(bob);
            side.Link("orders", order);
        });

        await Assert.That(ParityScenario.Differences(simulate, printed)).IsEmpty();
        foreach (var outcomes in new[] { simulate, printed }) {
            await Assert.That(outcomes[4].Success).IsFalse()
                .Because($"the second link was accepted: {outcomes[4].Step}");
            await Assert.That(outcomes[4].FailureKind).IsEqualTo("Link")
                .Because($"failure kind: {outcomes[4].FailureKind}");
            await Assert.That(outcomes[4].Message).Contains("Order").Because("the part is named");
            await Assert.That(outcomes[4].Message).Contains("Customer").Because("both owners are named");
            await Assert.That(outcomes[4].FailurePath).IsEqualTo("Customer.orders");
            await Assert.That(outcomes[4].State["orders"]).IsEqualTo("0");
        }
    }
}