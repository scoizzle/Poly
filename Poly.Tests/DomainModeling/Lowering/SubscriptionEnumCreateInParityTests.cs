namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #509: a subscription's <c>create in fines { Why: Late }</c> reads <c>Late</c> as an enum member
/// (a value), not as a property of the subscriber, and both hosts run it to the same created Fine.
/// </summary>
public class SubscriptionEnumCreateInParityTests {
    [Test]
    public async Task SubscriptionCreateIn_WhenEnumMemberInitializer_SimAndCSharpAgree() {
        var outcomes = await ParityScenario.FromDsl("""
            domain Probe
            Reason: enum { Late, Broken }
            Fine: entity {
              Why: Reason
            }
            Loan: entity {
              Active: stage {
                Slip: action { transition to Done }
              }
              Done: stage { }
            }
            Member: entity {
              fines: many Fine
              loans: many Loan
              Mk: action { create in loans { } }
              when loans Done {
                create in fines { Why: Late }
              }
            }
            """, "ParitySubscriptionEnumCreateIn")
            .AssertAgree(side => {
                var member = side.Create("Member");
                side.Invoke("Mk");
                side.UseLinked("loans");
                side.Invoke("Slip");
                if (side.Outcomes[^1].Success) {
                    side.Use(member);
                    side.UseLinked("fines");
                }
            });

        await Assert.That(outcomes[3].Success).IsTrue()
            .Because($"the transition failed: {outcomes[3].ExceptionType} {outcomes[3].Message}");
        await Assert.That(outcomes[5].State["Why"]).IsEqualTo("Late");
    }
}