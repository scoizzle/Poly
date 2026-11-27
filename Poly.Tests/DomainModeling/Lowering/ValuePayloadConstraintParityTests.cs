using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #612 / #571: a value type's member constraints are checked at the action boundary in the
/// lowered module, so the simulator and the printed C# refuse the same payload with the same
/// Range/Length/Pattern failure a create uses, and a null payload is a failure, never an NRE.
/// </summary>
public sealed class ValuePayloadConstraintParityTests {
    const string PayloadDsl = """
        domain PayloadConstraints

        Fee: value {
          Rate: Decimal range(0, 1)
          Code: Text pattern("^[A-Z]{2}$")
        }

        Money: value {
          Amount: Decimal range(0, 1000)
          Currency: Text length(3, 3)
          Reference: Text pattern("^[A-Z]{3}$")
          Fee: Fee
        }

        Order: entity {
          Total: Decimal default(0)
          Paid: Boolean optional
          Pay: action (m: Money) {
            assign Paid to true
          }
        }
        """;

    const string ContractDsl = """
        domain PayloadConstraints

        Billing: contract internal billing v1 {
          ChargeRequest: value {
            Amount: Number range(0, 1000)
            Currency: Text length(3, 3)
          }
          Charge: outbound operation ChargeRequest
        }

        public entity Order {
          Total: Number default(0)
          Paid: Boolean optional
          public action Pay (request: ChargeRequest) {
            assign Paid to true
          }
        }

        ChargeOrder: bind Billing Charge to Pay request
        """;

    static Dictionary<string, object?> Payload(long amount = 10, string currency = "EUR",
        decimal rate = 0.5m, string code = "AB", string reference = "ABC") => new() {
            ["Amount"] = amount,
            ["Currency"] = currency,
            ["Reference"] = reference,
            ["Fee"] = new Dictionary<string, object?> { ["Rate"] = rate, ["Code"] = code },
        };

    static Task<IReadOnlyList<ParityOutcome>> Run(string dsl, string assembly, object? payload) =>
        ParityScenario.FromDsl(dsl, assembly).AssertAgree(side => {
            side.Create("Order");
            side.Invoke("Pay", ("m", payload));
        });

    [Test]
    public async Task PayloadMemberAboveItsRange_FailsTheSameOnBothHosts() {
        var outcomes = await Run(PayloadDsl, "PayloadAboveRange", Payload(amount: 5000));

        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].Message).Contains("'Amount' must be <= 1000.");
        await Assert.That(outcomes[1].FailureKind).IsEqualTo("Range");
    }

    [Test]
    public async Task PayloadMemberBelowItsRange_FailsTheSameOnBothHosts() {
        var outcomes = await Run(PayloadDsl, "PayloadBelowRange", Payload(amount: -1));

        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].Message).Contains("'Amount' must be >= 0.");
        await Assert.That(outcomes[1].FailureKind).IsEqualTo("Range");
    }

    [Test]
    public async Task PayloadTextOutsideItsLength_FailsTheSameOnBothHosts() {
        var outcomes = await Run(PayloadDsl, "PayloadLength", Payload(currency: "XX"));

        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].Message).Contains("'Currency' must be at least 3 characters.");
        await Assert.That(outcomes[1].FailureKind).IsEqualTo("Length");
    }

    [Test]
    public async Task PayloadTextOutsideItsPattern_FailsTheSameOnBothHosts() {
        var outcomes = await Run(PayloadDsl, "PayloadPattern", Payload(reference: "abc"));

        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].Message).Contains("'Reference' does not match the required pattern.");
        await Assert.That(outcomes[1].FailureKind).IsEqualTo("Pattern");
    }

    [Test]
    public async Task NestedValueMemberAboveItsRange_FailsTheSameOnBothHosts() {
        var outcomes = await Run(PayloadDsl, "PayloadNestedRange", Payload(rate: 2m));

        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].Message).Contains("'Rate' must be <= 1.");
        await Assert.That(outcomes[1].FailureKind).IsEqualTo("Range");
    }

    [Test]
    public async Task NestedValueMemberOutsideItsPattern_FailsTheSameOnBothHosts() {
        var outcomes = await Run(PayloadDsl, "PayloadNestedPattern", Payload(code: "ab"));

        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].Message).Contains("'Code' does not match the required pattern.");
        await Assert.That(outcomes[1].FailureKind).IsEqualTo("Pattern");
    }

    [Test]
    public async Task NullPayload_IsAResultFailureNotAnNre() {
        var outcomes = await Run(PayloadDsl, "PayloadNull", null);

        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].ExceptionType).IsNull();
        await Assert.That(outcomes[1].Message).Contains("'m' is required.");
        await Assert.That(outcomes[1].FailureKind).IsEqualTo("Required");
    }

    [Test]
    public async Task ValidPayload_StillRunsTheAction() {
        var outcomes = await Run(PayloadDsl, "PayloadValid", Payload(amount: 12));

        await Assert.That(outcomes[1].Success).IsTrue().Because(outcomes[1].Message ?? "");
        await Assert.That(outcomes[1].State["Paid"]).IsEqualTo("True");
    }

    [Test]
    public async Task ContractDeclaredPayloadMemberAboveItsRange_FailsTheSameOnBothHosts() {
        var outcomes = await ParityScenario.FromDsl(ContractDsl, "PayloadContractRange")
            .AssertAgree(side => {
                side.Create("Order");
                side.Invoke("Pay", ("request", new Dictionary<string, object?> {
                    ["Amount"] = 5000L,
                    ["Currency"] = "EUR",
                }));
            });

        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].Message).Contains("'Amount' must be <= 1000.");
        await Assert.That(outcomes[1].FailureKind).IsEqualTo("Range");
    }
}