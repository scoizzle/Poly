using System.Globalization;

using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// T-18 characterisation: DSL forms whose operands reach the lowered-tree compatibility analysis, defined then run on
/// both hosts (the interpreter and the printed C#). T-18 makes that analysis read the type provider, and none of
/// these outcomes may change. A fractional DSL literal is a decimal at the source, so each literal row is Decimal
/// against Decimal.
/// </summary>
public class CompatibilityDslParityTests {
    const string Literals = """
        domain Lit
        Item: entity {
          Total: Decimal
          Price: Decimal
          Discounted: policy { Total * 0.9 > 0 }
          Cheap: policy { Price < 0.5 }
          Big: policy { Price > 150.5 }
          Scaled: policy { Price * 1.5 > 10 }
          Topped: policy { Total + 0.25 >= 1 }
          Trimmed: policy { Price - 0.1 <= 0 }
          Halved: policy { Total / 0.5 == 4 }
        }
        """;

    [Test]
    [Arguments("Discounted", "True")]
    [Arguments("Cheap", "False")]
    [Arguments("Big", "False")]
    [Arguments("Scaled", "True")]
    [Arguments("Topped", "True")]
    [Arguments("Trimmed", "False")]
    [Arguments("Halved", "True")]
    public async Task DecimalLiteralPolicy_BothHostsAgree_AndAnswerAsDecimal(string policy, string expected) {
        var outcomes = await ParityScenario.FromDsl(Literals, "T18Literal" + policy).AssertAgree(side => {
            side.Create("Item", ("Total", 2m), ("Price", 7.5m));
            side.EvaluatePolicy(policy);
        });
        await Assert.That(outcomes[1].Success).IsTrue();
        await Assert.That(outcomes[1].State[policy]).IsEqualTo(expected);
    }

    const string TextOrdering = """
        domain Ord
        Pair: entity {
          A: Text
          B: Text
          Before: policy { A < B }
          After: policy { A > B }
        }
        """;

    /// <summary>Text ordering runs on the interpreter only: the printed C# has no <c>string &lt; string</c> (next test).</summary>
    [Test]
    [Arguments("Before", "True")]
    [Arguments("After", "False")]
    public async Task TextOrderingPolicy_Interpreter_AnswersOrdinally(string policy, string expected) {
        var domain = EvolvedDomain.FromDsl(TextOrdering).Domain;
        RuntimeAnalysisCache.Bind(domain, ParitySessions.Create(domain.Extensions.Count > 0 ? domain.Extensions : ExtensionCatalog.ProductAuthoring));
        var side = new SimulateSide(domain);
        side.Create("Pair", ("A", "alpha"), ("B", "beta"));
        side.EvaluatePolicy(policy);
        await Assert.That(side.Outcomes[1].Success).IsTrue();
        await Assert.That(side.Outcomes[1].State[policy]).IsEqualTo(expected);
    }

    [Test]
    public async Task TextOrderingPolicy_PrintedHost_RefusesToCompile() {
        await Assert.That(() => ParityScenario.FromDsl(TextOrdering, "T18TextPrinted"))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("error CS0019: Operator '<' cannot be applied to operands of type 'string' and 'string'");
    }

    static string DateOffset(string type, string op) => $$"""
        domain Cal
        uses temporal
        Plan: entity {
          Start: {{type}}
          Days: Decimal
          Due: {{type}}
          Shift: action { assign Due to Start {{op}} Days }
        }
        """;

    /// <summary>
    /// Date ± Decimal passes the domain analyzer (its temporal offset permission, T-10), and the domain lowering emits
    /// <c>Start.AddDays((Int32)Days)</c>: no Date-with-Decimal operator node reaches the lowered compatibility analysis.
    /// Pinned as each host behaves today. Subtraction lowers to <c>AddDays((Int32)-Days)</c>, and the interpreter
    /// throws on it while the printed C# runs; that is pinned as it is, not as it should be.
    /// </summary>
    [Test]
    [Arguments("Date", "+", "simulate: success=True; exception=none; message=; Due=03/01/2024 | printed: success=True; exception=none; message=; Due=03/01/2024")]
    [Arguments("Date", "-", "simulate: success=False; exception=IndexOutOfRangeException; message=Index was outside the bounds of the array.; Due=<state not read> | printed: success=True; exception=none; message=; Due=02/26/2024")]
    public async Task DateOffsetByDecimal_BothHosts_AsToday(string type, string op, string expected) {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try {
            object start = type == "Date" ? new DateOnly(2024, 2, 28) : new DateTime(2024, 2, 28, 10, 0, 0);
            object due = type == "Date" ? new DateOnly(2000, 1, 1) : new DateTime(2000, 1, 1);
            var scenario = ParityScenario.FromDsl(DateOffset(type, op), $"T18Offset{type}{(op == "+" ? "Add" : "Sub")}",
                recordedExceptions: ["IndexOutOfRangeException"]);
            var (simulate, printed) = scenario.Run(side => {
                side.Create("Plan", ("Start", start), ("Days", 2m), ("Due", due));
                side.Invoke("Shift");
            });
            await Assert.That($"simulate: {Describe(simulate[1])} | printed: {Describe(printed[1])}").IsEqualTo(expected);
        }
        finally {
            CultureInfo.CurrentCulture = culture;
        }
    }

    /// <summary>
    /// DateTime ± Decimal: the lowering passes the decimal to <c>DateTime.AddDays(double)</c>. The printed C# does not
    /// compile, and the interpreter's compile is refused by the lowered analysis's invoke check (not an operator check).
    /// </summary>
    [Test]
    [Arguments("+", "success=False; exception=InvalidOperationException; message=VM compile rejected: AST member body analysis reported errors: no matching member 'AddDays' on 'DateTime' for invoke with 1 argument(s); Due=<state not read>")]
    [Arguments("-", "success=False; exception=InvalidOperationException; message=VM compile rejected: AST member body analysis reported errors: no matching member 'AddDays' on 'DateTime' for invoke with 1 argument(s); Due=<state not read>")]
    public async Task DateTimeOffsetByDecimal_Interpreter_AsToday(string op, string expected) {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try {
            var domain = EvolvedDomain.FromDsl(DateOffset("DateTime", op)).Domain;
            RuntimeAnalysisCache.Bind(domain, ParitySessions.Create(domain.Extensions.Count > 0 ? domain.Extensions : ExtensionCatalog.ProductAuthoring));
            var side = new SimulateSide(domain, new HashSet<string>(["IndexOutOfRangeException", "InvalidCastException"], StringComparer.Ordinal));
            side.Create("Plan", ("Start", new DateTime(2024, 2, 28, 10, 0, 0)), ("Days", 2m), ("Due", new DateTime(2000, 1, 1)));
            side.Invoke("Shift");
            await Assert.That(Describe(side.Outcomes[1])).IsEqualTo(expected);
        }
        finally {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Test]
    [Arguments("+")]
    [Arguments("-")]
    public async Task DateTimeOffsetByDecimal_PrintedHost_RefusesToCompile(string op) {
        await Assert.That(() => ParityScenario.FromDsl(DateOffset("DateTime", op), "T18OffsetDateTimePrinted" + (op == "+" ? "Add" : "Sub")))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("error CS1503: Argument 1: cannot convert from 'decimal' to 'double'");
    }

    /// <summary>A step that threw reports no state (the harness default), so its Due is named as not read.</summary>
    static string Describe(ParityOutcome outcome) =>
        $"success={outcome.Success}; exception={outcome.ExceptionType ?? "none"}; message={outcome.Message}; Due={(outcome.ExceptionType is null ? outcome.State["Due"] : "<state not read>")}";
}