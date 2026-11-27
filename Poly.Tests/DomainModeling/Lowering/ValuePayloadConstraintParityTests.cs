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

    const string MissingMemberDsl = """
        domain PayloadReview

        Tier: enum { Gold, Silver }

        Money: value {
          Amount: Decimal range(0, 1000)
          Currency: Text length(3, 3)
          Level: Tier
        }

        Order: entity {
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
    public async Task PayloadMissingRequiredTextMember_FailsRequiredOnBothHosts() {
        var outcomes = await ParityScenario.FromDsl(MissingMemberDsl, "PayloadMissingText")
            .AssertAgree(side => {
                side.Create("Order");
                side.Invoke("Pay", ("m", new Dictionary<string, object?> {
                    ["Amount"] = 10L,
                    ["Level"] = "Gold",
                }));
            });

        await Assert.That(outcomes[1].ExceptionType).IsNull();
        await Assert.That(outcomes[1].FailureKind).IsEqualTo("Required");
        await Assert.That(outcomes[1].Message).Contains("'Currency' is required.");
    }

    [Test]
    public async Task PayloadMissingEnumMember_FailsRequiredOnBothHosts() {
        var outcomes = await ParityScenario.FromDsl(MissingMemberDsl, "PayloadMissingEnum")
            .AssertAgree(side => {
                side.Create("Order");
                side.Invoke("Pay", ("m", new Dictionary<string, object?> {
                    ["Amount"] = 10L,
                    ["Currency"] = "EUR",
                }));
            });

        await Assert.That(outcomes[1].ExceptionType).IsNull();
        await Assert.That(outcomes[1].FailureKind).IsEqualTo("Required");
        await Assert.That(outcomes[1].Message).Contains("'Level' is required.");
    }

    const string BinaryMissingMemberDsl = """
        domain PayloadBinary

        Blob: value {
          Data: Binary
          Label: Text
        }

        Order: entity {
          Paid: Boolean optional
          Pay: action (b: Blob) {
            assign Paid to true
          }
        }
        """;

    /// <summary>
    /// B1: an absent required Binary member reads null on both hosts, so the required guard
    /// refuses it; the simulator used to read <c>Missing</c>, pass the guard, and succeed
    /// where the printed C# failed.
    /// </summary>
    [Test]
    public async Task PayloadMissingBinaryMember_FailsRequiredOnBothHosts() {
        var outcomes = await ParityScenario.FromDsl(BinaryMissingMemberDsl, "PayloadMissingBinary")
            .AssertAgree(side => {
                side.Create("Order");
                side.Invoke("Pay", ("b", new Dictionary<string, object?> { ["Label"] = "x" }));
            });

        await Assert.That(outcomes[1].ExceptionType).IsNull();
        await Assert.That(outcomes[1].FailureKind).IsEqualTo("Required");
        await Assert.That(outcomes[1].Message).Contains("'Data' is required.");
    }

    const string NestedMissingMemberDsl = """
        domain PayloadNested

        Money: value {
          Amount: Decimal range(0, 1000)
          Currency: Text length(3, 3)
        }

        Payment: value {
          Price: Money
          Note: Text
        }

        Order: entity {
          Paid: Boolean optional
          Pay: action (p: Payment) {
            assign Paid to true
          }
        }
        """;

    /// <summary>
    /// B2: an absent required nested value member reads null on both hosts, so the required
    /// guard refuses it instead of the simulator throwing the <c>Missing</c> cast.
    /// </summary>
    [Test]
    public async Task PayloadMissingNestedValueMember_FailsRequiredOnBothHosts() {
        var outcomes = await ParityScenario.FromDsl(NestedMissingMemberDsl, "PayloadMissingNested")
            .AssertAgree(side => {
                side.Create("Order");
                side.Invoke("Pay", ("p", new Dictionary<string, object?> { ["Note"] = "n" }));
            });

        await Assert.That(outcomes[1].ExceptionType).IsNull();
        await Assert.That(outcomes[1].FailureKind).IsEqualTo("Required");
        await Assert.That(outcomes[1].Message).Contains("'Price' is required.");
    }

    const string OptionalEntityReadDsl = """
        domain OptionalEntityRead

        Tier: enum { Gold, Silver }

        Customer: entity {
          Name: Text
          Note: Text optional
          Level: Tier optional
          NoteAbsent: policy { Note is null }
          LevelAbsent: policy { Level is null }
        }
        """;

    /// <summary>
    /// S-a: the absent-key rule is VM-wide, not payload-only. An unset optional Text entity
    /// property reads null on every modeled read.
    /// </summary>
    [Test]
    public async Task UnsetOptionalEntityText_ReadsNullOnBothHosts() {
        var outcomes = await ParityScenario.FromDsl(OptionalEntityReadDsl, "OptionalEntityText")
            .AssertAgree(side => {
                side.Create("Customer", ("Name", "a"));
                side.EvaluatePolicy("NoteAbsent");
            });

        await Assert.That(outcomes[1].State["NoteAbsent"]).IsEqualTo("True")
            .Because("an absent Text is null on every modeled read, not just a payload member");
    }

    /// <summary>
    /// S-a: an unset optional enum entity property reads null on every modeled read, not a
    /// <c>Missing</c> member the enum guard would report.
    /// </summary>
    [Test]
    public async Task UnsetOptionalEntityEnum_ReadsNullOnBothHosts() {
        var outcomes = await ParityScenario.FromDsl(OptionalEntityReadDsl, "OptionalEntityEnum")
            .AssertAgree(side => {
                side.Create("Customer", ("Name", "a"));
                side.EvaluatePolicy("LevelAbsent");
            });

        await Assert.That(outcomes[1].State["LevelAbsent"]).IsEqualTo("True")
            .Because("an absent enum is null on every modeled read, not just a payload member");
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