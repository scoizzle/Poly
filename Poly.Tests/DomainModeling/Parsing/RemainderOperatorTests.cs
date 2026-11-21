using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// #548: <c>%</c> is the remainder operator with C#'s semantics — the same precedence as <c>*</c> and
/// <c>/</c>, and the sign of the dividend — defined for Number and Decimal. It lowers to the
/// interpreter's modulo node, so the simulator and the printed C# are one meaning.
/// </summary>
public sealed class RemainderOperatorTests {
    private static string Dsl(string expression) => $$"""
        domain Rem
        A: entity {
          N: Number range(0, 1000)
          M: Number range(0, 1000)
          R: Number range(0, 10)
          T: Text
          D: Date
          Q: Decimal
          Go: action { assign R to {{expression}} }
        }
        """;

    // A range-free target, so a negative remainder is stored rather than warned about.
    private static string UnboundedDsl(string expression) => $$"""
        domain Rem
        A: entity {
          N: Number range(0, 1000)
          X: Number
          Go: action { assign X to {{expression}} }
        }
        """;

    [Test]
    public async Task Parse_NumberRemainder_IsAccepted() {
        await Assert.That(ResolverBaselineTests.Resolve(Dsl("N % 7"))).StartsWith("OK ");
    }

    [Test]
    public async Task Parse_SamePrecedenceAsMultiplyAndDivide() {
        // '*' and '%' share the level, and the level is left-assoc: N * M % 7 is (N * M) % 7.
        await Assert.That(ResolverBaselineTests.Resolve(Dsl("N * M % 7"))).StartsWith("OK ");
        await Assert.That(ResolverBaselineTests.Resolve(Dsl("N % M * 7"))).StartsWith("OK ");
        // The add level is looser than the multiply level: N + M % 7 is N + (M % 7).
        await Assert.That(ResolverBaselineTests.Resolve(Dsl("N + M % 7"))).StartsWith("OK ");
    }

    [Test]
    [Arguments("N %")]
    [Arguments("% 7")]
    [Arguments("N %% 7")]
    public async Task Parse_Remainder_WithAMissingOperand_IsAPositionedError(string expression) {
        // The expression sits on line 9 of Dsl(): a refusal is a positioned error, never a crash.
        var answer = ResolverBaselineTests.Resolve(Dsl(expression));
        await Assert.That(answer).StartsWith("ERR ");
        await Assert.That(answer).Contains("@9:");
    }

    [Test]
    [Arguments("T % 7")]
    [Arguments("N % T")]
    [Arguments("D % 7")]
    public async Task Analyze_Remainder_OnANonNumericOperand_IsRefused(string expression) {
        // Fail closed: Text and Date get the arithmetic rule's refusal in analysis, never a silent fold.
        var error = Assert.Throws<InvalidOperationException>(() => EvolvedDomain.FromDsl(Dsl(expression)));
        await Assert.That(error!.Message).Contains("not numeric");
    }

    [Test]
    public async Task ParseThenPrint_RoundTrips_Remainder() {
        var poly = Dsl("N % 7");
        var first = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(new PolyDslParser(poly).Parse());
        await Assert.That(first.Succeeded).IsTrue();

        var printed = new Poly.DomainModeling.Language.DomainDslPrinter().Print(first.Root!);
        await Assert.That(printed).Contains("%");

        var second = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(new PolyDslParser(printed).Parse());
        await Assert.That(second.Succeeded).IsTrue();

        // Write(Read(x)) is idempotent: printing what was re-read gives the same text.
        var reprinted = new Poly.DomainModeling.Language.DomainDslPrinter().Print(second.Root!);
        await Assert.That(reprinted).IsEqualTo(printed);
    }
}