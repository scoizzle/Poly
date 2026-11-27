using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// Issue #501: a negative literal (<c>assign Balance to -3</c>) and unary minus on an expression
/// (<c>-Balance</c>) parse, analyse, simulate and print, on both hosts. Unary minus binds tighter than
/// <c>*</c> and <c>/</c> and looser than a primary, as C# does, so <c>-2 * 3</c> is <c>(-2) * 3</c> and
/// <c>5 + -3</c> negates only <c>3</c>. A non-numeric operand is refused with a positioned error.
/// </summary>
public sealed class UnaryMinusExpressionTests {
    private const string Wallet = """
        domain Wallet
        Account: entity {
          Balance: Number range(-1000, 1000) default(0)
          Fee: Decimal default(0.5)
          Label: Text default("x")
          Due: Decimal default(0.25)
          Credit: action { assign Balance to -3 }
          Reverse: action { assign Balance to -Balance }
          Combined: action { assign Balance to 5 + -3 }
          Precedence: action { assign Balance to -2 * 3 }
          Grouped: action { assign Balance to -(1 + 2) }
          Double: action { assign Balance to - -3 }
          Refund: action { assign Due to -Due }
          Discount: action { assign Due to -0.25 }
        }
        """;

    private static EvolutionResult Apply(string poly) =>
        new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(poly).Parse());

    /// <summary>Errors and warnings only: the analyser's authoring hints are not this file's subject.</summary>
    private static List<string> Problems(EvolutionResult result) =>
        [.. result.Analysis.Diagnostics
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(d => $"{d.Severity} {d.Code}: {d.Message}")];

    /// <summary>The assigned expression, as the printer writes it back: the shape a reader sees.</summary>
    private static string AssignedText(string actionName, string poly = Wallet) {
        var entity = Apply(poly).Root!.Types.OfType<Entity>().Single();
        var action = entity.Actions.Single(a => a.Name == actionName);
        var value = action.Effects.OfType<AssignEffect>().Single().Value;
        return new DomainDslPrinter().PrintExpressionText(value);
    }

    // ── parse ───────────────────────────────────────────────────

    [Test]
    public async Task Parse_NegativeLiteral_PrintsAsANegativeLiteral() {
        await Assert.That(AssignedText("Credit")).IsEqualTo("-3");
    }

    [Test]
    public async Task Parse_NegatedProperty_PrintsAsANegatedRead() {
        await Assert.That(AssignedText("Reverse")).IsEqualTo("-Balance");
    }

    /// <summary><c>5 + -3</c>: the prefix binds to the operand of the sum, never to the sum.</summary>
    [Test]
    public async Task Parse_PrefixAfterBinaryOperator_NegatesOnlyTheRightOperand() {
        await Assert.That(AssignedText("Combined")).IsEqualTo("(5 + -3)");
    }

    /// <summary><c>-2 * 3</c>: unary minus binds tighter than <c>*</c>, as C# does.</summary>
    [Test]
    public async Task Parse_PrefixAndProduct_NegatesTheLeftFactorOnly() {
        await Assert.That(AssignedText("Precedence")).IsEqualTo("(-2 * 3)");
    }

    [Test]
    public async Task Parse_DoubleNegation_Parses() {
        await Assert.That(AssignedText("Double")).IsEqualTo("-(-3)");
    }

    // ── analyze ─────────────────────────────────────────────────

    [Test]
    public async Task Analyze_WholeWalletDomain_IsClean() {
        var result = Apply(Wallet);

        await Assert.That(Problems(result)).IsEmpty();
    }

    /// <summary>
    /// A negated value still has a range, so a constrained write of <c>-3</c> is verified rather than reported
    /// as an operator the interval flow cannot model.
    /// </summary>
    [Test]
    public async Task Analyze_NegativeLiteralIntoRangedProperty_HasNoUnverifiableWriteWarning() {
        var result = Apply("""
            domain Wallet
            Account: entity {
              Balance: Number range(-1000, 1000) default(0)
              Credit: action { assign Balance to -3 }
            }
            """);

        await Assert.That(Problems(result)).IsEmpty();
    }

    /// <summary>
    /// A duration is refused, not silently negated: Duration arithmetic is #550's and nothing here may ship a
    /// shape the hosts do not agree on. The message names the operand's type.
    /// </summary>
    [Test]
    public async Task Analyze_NegatingDuration_IsRefused() {
        var result = Apply("""
            domain Wallet
            uses temporal
            Account: entity {
              Wait: Duration default(1 Hours)
              Break: action { assign Wait to -Wait }
            }
            """);

        var error = result.Analysis.Diagnostics.Single(d => d.Severity == DiagnosticSeverity.Error);
        await Assert.That(error.Message).Contains("unary '-'");
        await Assert.That(error.Message).Contains("duration");
    }

    [Test]
    public async Task Analyze_NegatingText_ReportsWhatTheOperandIs() {
        var result = Apply("""
            domain Wallet
            Account: entity {
              Label: Text default("x")
              Break: action { assign Label to -Label }
            }
            """);

        var error = result.Analysis.Diagnostics.Single(d => d.Severity == DiagnosticSeverity.Error);
        await Assert.That(error.Message).Contains("unary '-'");
        await Assert.That(error.Message).Contains("Text");
    }

    // ── print ───────────────────────────────────────────────────

    [Test]
    public async Task WriteRead_Negations_IsIdempotent() {
        var first = new DomainDslPrinter().Print(Apply(Wallet).Root!);

        await Assert.That(first).Contains("assign Balance to -3");
        await Assert.That(first).Contains("assign Balance to -Balance");
        await Assert.That(first).Contains("assign Balance to -(1 + 2)");
        var second = new DomainDslPrinter().Print(Apply(first).Root!);
        await Assert.That(second).IsEqualTo(first);
    }

    // ── evaluate on both sides (simulate + printed C#) ─────────

    [Test]
    public async Task Evaluate_NegativeLiteral_StoresMinusThreeOnBothSides() {
        var outcomes = await ParityScenario.FromDsl(Wallet, "UnaryMinusLiteral").AssertAgree(side => {
            side.Create("Account");
            side.Invoke("Credit");
        });
        await Assert.That(outcomes[1].State["Balance"]).IsEqualTo("-3");
    }

    [Test]
    public async Task Evaluate_NegatedProperty_FlipsTheSignOnBothSides() {
        var outcomes = await ParityScenario.FromDsl(Wallet, "UnaryMinusProperty").AssertAgree(side => {
            side.Create("Account");
            side.Invoke("Credit");
            side.Invoke("Reverse");
        });
        await Assert.That(outcomes[2].State["Balance"]).IsEqualTo("3");
    }

    /// <summary>
    /// #832's cell: unary minus on a Decimal is the operand's own negation, never the handle's (the VM once
    /// answered -1 for -0.1).
    /// </summary>
    [Test]
    public async Task Evaluate_NegatedDecimal_IsTheOperandsNegationOnBothSides() {
        var outcomes = await ParityScenario.FromDsl(Wallet, "UnaryMinusDecimal").AssertAgree(side => {
            side.Create("Account");
            side.Invoke("Refund");
            side.Invoke("Discount");
        });
        await Assert.That(outcomes[1].State["Due"]).IsEqualTo("-0.25");
        await Assert.That(outcomes[2].State["Due"]).IsEqualTo("-0.25");
    }

    [Test]
    public async Task Evaluate_PrecedenceAndGrouping_AgreeWithTheOperators() {
        var outcomes = await ParityScenario.FromDsl(Wallet, "UnaryMinusPrecedence").AssertAgree(side => {
            side.Create("Account");
            side.Invoke("Combined");
            side.Invoke("Precedence");
            side.Invoke("Grouped");
        });
        await Assert.That(outcomes[1].State["Balance"]).IsEqualTo("2");
        await Assert.That(outcomes[2].State["Balance"]).IsEqualTo("-6");
        await Assert.That(outcomes[3].State["Balance"]).IsEqualTo("-3");
    }
}