using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Issue #491, ruling Q6 (R5): a Decimal expression stored into a Number property rounds its fraction
/// (<c>3 + 0.5</c> stored as 4). No proof that the rounded value is safe exists yet ("proves it safe" is
/// T-12's open question), so analysis refuses the narrowing instead of rounding silently — the T-08
/// fail-closed baseline. Every door that stores a value goes through the one assignability rule:
/// an assign, a create initializer, a function argument and a default.
/// </summary>
public sealed class DecimalToNumberAssignTests {
    private const string Bank = """
        domain Bank
        Account: entity {
          N: Number default(3)
          D: Decimal default(0.5)
          Sum: Number default(0)
          Exact: Decimal default(0)
          Mix: action { assign N to N + D }
          IntoNumber: action { assign Sum to N + N }
          IntoDecimal: action { assign Exact to N + D }
          Product: action { assign Exact to D * D }
        }
        """;

    private static EvolutionResult Apply(string poly) =>
        new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(poly).Parse());

    private static List<string> Errors(EvolutionResult result) =>
        [.. result.Analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Message)];

    // ── the repro ───────────────────────────────────────────────

    [Test]
    public async Task Assign_DecimalArithmeticIntoNumber_IsAnError() {
        var errors = Errors(Apply(Bank));

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0]).Contains("Decimal");
        await Assert.That(errors[0]).Contains("Number");
        await Assert.That(errors[0]).Contains("'N'");
    }

    /// <summary>The two arithmetic results that do not round are untouched: Number + Number into Number, and any Decimal into Decimal.</summary>
    [Test]
    public async Task Assign_NonRoundingArithmetic_StaysAccepted() {
        var errors = Errors(Apply(Bank));

        await Assert.That(errors.Select(m => m)).DoesNotContain(m => m.Contains("'Sum'") || m.Contains("'Exact'"));
    }

    /// <summary>A Decimal property read on its own is the same rule, and already refused; it must stay refused.</summary>
    [Test]
    public async Task Assign_DecimalPropertyReadIntoNumber_IsAnError() {
        var errors = Errors(Apply("""
            domain Bank
            Account: entity {
              N: Number default(0)
              D: Decimal default(0.5)
              Mix: action { assign N to D }
            }
            """));

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0]).Contains("cannot assign 'Decimal' to 'Number'");
    }

    [Test]
    public async Task Assign_NumberArithmeticIntoNumber_IsAccepted() {
        var errors = Errors(Apply("""
            domain Bank
            Account: entity {
              N: Number default(3)
              M: Number default(4)
              Sum: Number default(0)
              Mix: action { assign Sum to N + M * 2 }
            }
            """));

        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Assign_DecimalArithmeticIntoDecimal_IsAccepted() {
        var errors = Errors(Apply("""
            domain Bank
            Account: entity {
              N: Number default(3)
              D: Decimal default(0.5)
              Exact: Decimal default(0)
              Mix: action { assign Exact to N + D }
            }
            """));

        await Assert.That(errors).IsEmpty();
    }

    // ── the other doors ─────────────────────────────────────────

    [Test]
    public async Task CreateInitializer_DecimalArithmeticIntoNumber_IsAnError() {
        var errors = Errors(Apply("""
            domain Bank
            Account: entity {
              N: Number default(3)
              D: Decimal default(0.5)
              Make: action { create Account { N: N + D } }
            }
            """));

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0]).Contains("cannot assign 'Decimal' to 'Number'");
    }

    [Test]
    public async Task FunctionArgument_DecimalArithmeticIntoNumber_IsAnError() {
        var errors = Errors(Apply("""
            domain Bank
            Account: entity {
              N: Number default(3)
              D: Decimal default(0.5)
              Pass: action { assign N to Keep(N + D) }
            }
            function Keep(a: Number): Number = a
            """));

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0]).Contains("expects 'Number'");
        await Assert.That(errors[0]).Contains("got 'Decimal'");
    }

    /// <summary>A Number arithmetic result into a Decimal function parameter still widens.</summary>
    [Test]
    public async Task FunctionArgument_NumberArithmeticIntoDecimal_IsAccepted() {
        var errors = Errors(Apply("""
            domain Bank
            Account: entity {
              N: Number default(3)
              M: Number default(4)
              Exact: Decimal default(0)
              Pass: action { assign Exact to Scale(N + M) }
            }
            function Scale(a: Decimal): Decimal = a * 2
            """));

        await Assert.That(errors).IsEmpty();
    }
}