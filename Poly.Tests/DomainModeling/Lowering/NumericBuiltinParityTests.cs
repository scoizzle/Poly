using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #547: the built-in numeric functions <c>Round</c>, <c>Abs</c>, <c>Min</c>, <c>Max</c>, <c>Floor</c> and
/// <c>Ceiling</c> are legal calls. Each lowers to one <c>System.Math</c> call that the interpreter and the
/// printed C# share, so a row runs the same action on both sides and pins the value: a decimal can be
/// rounded to cents, and the midpoint goes away from zero (the money rule), not to even.
/// </summary>
public class NumericBuiltinParityTests {
    static string Dsl(string expression, string target = "Out", string declarations = "") => $$"""
        domain Money
        Invoice: entity {
          Bal: Decimal range(-1000, 1000)
          Neg: Decimal range(-1000, 1000)
          Low: Decimal range(-1000, 1000)
          High: Decimal range(-1000, 1000)
          Whole: Number range(-1000, 1000)
          Out: Decimal range(-1000, 1000)
          Apply: action { assign {{target}} to {{expression}} }
        }
        {{declarations}}
        """;

    static void Apply(ParitySide side) {
        side.Create("Invoice",
            ("Bal", 123.45m), ("Neg", -1.005m), ("Low", 3.5m), ("High", 7.25m), ("Whole", 7L), ("Out", 0m));
        side.Invoke("Apply");
    }

    [Test]
    [Arguments("Round(Bal * 0.015, 2)", "Out", "1.85")]
    [Arguments("Round(1.005, 2)", "Out", "1.01")]
    [Arguments("Round(Neg, 2)", "Out", "-1.01")]
    [Arguments("Abs(Neg)", "Out", "1.005")]
    [Arguments("Floor(Bal)", "Out", "123")]
    [Arguments("Ceiling(Bal)", "Out", "124")]
    [Arguments("Min(Low, High)", "Out", "3.5")]
    [Arguments("Max(Low, High)", "Out", "7.25")]
    [Arguments("Min(Bal, 100)", "Out", "100")]
    [Arguments("Max(Bal, 100)", "Out", "123.45")]
    [Arguments("Round(Whole, 2)", "Whole", "7")]
    [Arguments("Floor(Whole)", "Whole", "7")]
    [Arguments("Ceiling(Whole)", "Whole", "7")]
    [Arguments("Abs(Whole)", "Whole", "7")]
    public async Task Invoke_WhenNumericBuiltin_BothSidesAgreeOnTheValue(
        string expression, string target, string expected) {
        var outcomes = await ParityScenario.FromDsl(Dsl(expression, target), "ParityNumericBuiltin")
            .AssertAgree(Apply);
        await Assert.That(outcomes[0].Success).IsTrue();
        await Assert.That(outcomes[1].Success).IsTrue();
        await Assert.That(outcomes[1].State[target]).IsEqualTo(expected);
    }

    /// <summary>The printed C# names the Math call the interpreter also runs, so both sides execute one meaning.</summary>
    [Test]
    public async Task PrintedCsharp_WhenRoundToCents_CallsMathRoundAwayFromZero() {
        var printed = Print(Dsl("Round(Bal * 0.015, 2)"));
        await Assert.That(printed).Contains("System.Math.Round(");
        await Assert.That(printed).Contains("System.MidpointRounding.AwayFromZero");
    }

    /// <summary>The printed module re-reads to stable text that still names the Math call: the lowered call round-trips.</summary>
    [Test]
    public async Task PrintedCsharp_WhenRoundIsPrinted_ReReadsToTheSameText() {
        var printed = Print(Dsl("Round(Bal * 0.015, 2)"));
        var once = CSharpLanguage.Write(CSharpLanguage.Read(printed));
        var twice = CSharpLanguage.Write(CSharpLanguage.Read(once));
        await Assert.That(twice).IsEqualTo(once);
        await Assert.That(once).Contains("System.Math.Round(");
    }

    /// <summary>A declared function of the same name wins, as a contract endpoint does.</summary>
    [Test]
    public async Task Call_WhenADeclaredFunctionNamesRound_DeclaredBodyRuns() {
        var declarations = "function Round(a: Decimal, b: Number): Decimal = a";
        var outcomes = await ParityScenario.FromDsl(
                Dsl("Round(Bal, 2)", declarations: declarations), "ParityNumericBuiltinDeclared")
            .AssertAgree(Apply);
        await Assert.That(outcomes[1].State["Out"]).IsEqualTo("123.45");
    }

    [Test]
    public async Task Analyzer_WhenRoundHasOneArgument_IsAnArityError() {
        await Assert.That(SingleError(Dsl("Round(Bal)")))
            .IsEqualTo("Function 'Round' expects 2 argument(s), got 1 (line 9, col 33).");
    }

    [Test]
    public async Task Analyzer_WhenDigitsAreText_NamesTheExpectedType() {
        await Assert.That(SingleError(Dsl("Round(Bal, \"2\")")))
            .IsEqualTo("Argument 'digits' of 'Round' expects a numeric type, got 'Text'.");
    }

    [Test]
    public async Task Analyzer_WhenDigitsAreDecimal_ExpectsAWholeNumber() {
        await Assert.That(SingleError(Dsl("Round(Bal, 2.5)")))
            .IsEqualTo("Argument 'digits' of 'Round' expects 'Number', got 'Decimal'.");
    }

    [Test]
    public async Task Analyzer_WhenDigitsAreOutOfRange_IsAPositionedError() {
        await Assert.That(SingleError(Dsl("Round(Bal, 29)")))
            .IsEqualTo("'Round' digits 29 is outside the supported range 0 to 28.");
    }

    [Test]
    public async Task Analyzer_WhenAnArgumentIsNotNumeric_NamesTheArgument() {
        await Assert.That(SingleError(Dsl("Abs(\"x\")")))
            .IsEqualTo("Argument 'value' of 'Abs' expects a numeric type, got 'Text'.");
    }

    /// <summary>
    /// <c>Min</c> and <c>Max</c> carry the hull of their operands' intervals, so a write whose bounds are inside
    /// the target's declared range is verified (no DMCS003 "cannot verify" warning), not left unknown.
    /// </summary>
    [Test]
    public async Task Analysis_WhenMinOrMaxOfBoundedProperties_IsWritten_TheWriteIsVerified() {
        foreach (var expression in new[] { "Min(Low, High)", "Max(Low, High)", "Abs(Low)" }) {
            var warnings = Diagnostics(Dsl(expression))
                .Where(d => d.Code is "DMCS003" or "DMEFF008")
                .Select(d => d.Message).ToList();
            await Assert.That(string.Join(" | ", warnings)).IsEqualTo("");
        }
    }

    private static IReadOnlyList<Diagnostic> Diagnostics(string dsl) =>
        new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse()).Analysis.Diagnostics;

    private static string Print(string dsl) {
        var builder = SessionBuilder.CreateEmpty();
        builder.LoadCore();
        builder.Load(new Poly.DomainModeling.Libraries.Storage.PersistenceEmitLibrary());
        var session = builder.Build();
        var changes = new PolyDslParser(dsl, session).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(changes, session: session);
        if (!evolved.Succeeded)
            throw new InvalidOperationException(string.Join("; ", evolved.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        return new Poly.Interpretation.CSharp.CSharpGenerator()
            .Generate(session.Lower(evolved.Root!, evolved.Analysis));
    }

    private static string SingleError(string dsl) =>
        Diagnostics(dsl)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Message)
            .Single();
}