using System.Globalization;

using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #548: the simulator and the printed C# agree on <c>%</c>, which is what one meaning means here. C#'s
/// remainder takes the sign of the dividend, so the negative case is the one a guess would get wrong.
/// </summary>
public class RemainderParityTests {
    private static string Dsl(string splitExpression) => $$"""
        domain Rem
        Item: entity {
          N: Number range(0, 1000)
          Divisor: Number range(1, 1000)
          Offset: Number
          Rest: Number
          Amount: Decimal
          Part: Decimal
          Go: action { assign Rest to N % Divisor }
          Split: action { assign Part to {{splitExpression}} }
          Signed: action { assign Rest to (Offset - 10) % 7 }
          DivisorSign: action { assign Rest to N % (Offset - 10) }
        }
        """;

    private static void Create(ParitySide side, long n, long divisor, long offset, decimal amount = 0m) =>
        side.Create("Item", ("N", n), ("Divisor", divisor), ("Offset", offset), ("Rest", 0L), ("Amount", amount), ("Part", 0m));

    [Test]
    [Arguments(0L, 7L, "0")]
    [Arguments(1L, 7L, "1")]
    [Arguments(7L, 7L, "0")]
    [Arguments(23L, 7L, "2")]
    [Arguments(1000L, 7L, "6")]
    [Arguments(1000L, 999L, "1")]
    public async Task Go_WhenNumberRemainder_BothSidesAgreeOnTheValue(long n, long divisor, string expected) {
        var outcomes = await ParityScenario.FromDsl(Dsl("Amount"), "ParityRemainder")
            .AssertAgree(side => { Create(side, n, divisor, 0); side.Invoke("Go"); });
        await Assert.That(outcomes[^1].Success).IsTrue();
        await Assert.That(outcomes[^1].State["Rest"]).IsEqualTo(expected);
    }

    [Test]
    [Arguments("0")]
    [Arguments("7")]
    [Arguments("10")]
    [Arguments("-7")]
    public async Task Split_WhenDecimalRemainder_BothSidesAgreeOnTheValue(string amountText) {
        var amount = decimal.Parse(amountText, CultureInfo.InvariantCulture);
        var outcomes = await ParityScenario.FromDsl(Dsl("Amount % 2.5"), "ParityRemainderDecimal")
            .AssertAgree(side => { Create(side, 0, 1, 0, amount); side.Invoke("Split"); });
        await Assert.That(outcomes[^1].Success).IsTrue();
        // The independent authority is the C# runtime.
        await Assert.That(decimal.Parse(outcomes[^1].State["Part"]!, CultureInfo.InvariantCulture))
            .IsEqualTo(amount % 2.5m);
    }

    [Test]
    public async Task Signed_WhenTheDividendIsNegative_BothSidesTakeTheDividendsSign() {
        // (0 - 10) % 7 is -3 in C#: the remainder takes the dividend's sign, never the divisor's.
        var outcomes = await ParityScenario.FromDsl(Dsl("Amount"), "ParityRemainderSign")
            .AssertAgree(side => { Create(side, 0, 7, 0); side.Invoke("Signed"); });
        await Assert.That(outcomes[^1].Success).IsTrue();
        await Assert.That(outcomes[^1].State["Rest"]).IsEqualTo("-3");
    }

    [Test]
    public async Task Signed_WhenTheDividendIsPositive_TheResultIsNot() {
        // (4 - 10) % 7 is -6.
        var outcomes = await ParityScenario.FromDsl(Dsl("Amount"), "ParityRemainderSignTwo")
            .AssertAgree(side => { Create(side, 0, 7, 4); side.Invoke("Signed"); });
        await Assert.That(outcomes[^1].Success).IsTrue();
        await Assert.That(outcomes[^1].State["Rest"]).IsEqualTo("-6");
    }

    [Test]
    public async Task DivisorSign_WhenTheDivisorIsNegative_TheResultStillFollowsTheDividend() {
        // 7 % -10 is 7: the divisor's sign never decides.
        var outcomes = await ParityScenario.FromDsl(Dsl("Amount"), "ParityRemainderDivisorSign")
            .AssertAgree(side => { Create(side, 7, 7, 0); side.Invoke("DivisorSign"); });
        await Assert.That(outcomes[^1].Success).IsTrue();
        await Assert.That(outcomes[^1].State["Rest"]).IsEqualTo("7");
    }
}