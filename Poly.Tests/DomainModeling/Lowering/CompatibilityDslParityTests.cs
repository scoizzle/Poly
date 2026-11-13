using System.Globalization;

using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Ontology;
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

    /// <summary>
    /// The DSL literal 0.5 is a decimal, so <c>Price &lt; 0.5</c> is Decimal against Decimal (the sibling, accepted). An
    /// API-built <c>Literal(0.5d)</c> is a double; the platform defines no Decimal &lt; Double, and the lowered analysis
    /// rejects the policy's compile (T-18, issue 815).
    /// </summary>
    [Test]
    public async Task DecimalProperty_AgainstApiBuiltDoubleLiteral_IsRejectedByTheLoweredAnalysis() {
        const string dsl = """
            domain LitApi
            Item: entity {
              Price: Decimal
              Cheap: policy { Price < 0.5 }
            }
            """;
        await Assert.That(Evaluate(EvolvedDomain.FromDsl(dsl).Domain)).IsEqualTo("success=True; answer=True");

        var domain = EvolvedDomain.FromDsl(dsl).Domain;
        var entity = domain.Types.OfType<Entity>().Single();
        var policy = entity.Policies.Single();
        var edited = policy with { Expression = new Comparison(DomainExpression.Property("Price"), DomainExpression.Literal(0.5d), ComparisonKind.LessThan) };
        entity = entity with { Policies = [edited] };
        domain = domain with { Types = [.. domain.Types.Select(t => t is Entity ? entity : t)] };
        await Assert.That(Evaluate(domain)).IsEqualTo(
            "success=False; message=VM compile rejected: comparison between incompatible types 'Decimal' and 'Double': no LessThan operator for these types");
    }

    static string Evaluate(Domain domain) {
        RuntimeAnalysisCache.Bind(domain, ParitySessions.Create(domain.Extensions.Count > 0 ? domain.Extensions : ExtensionCatalog.ProductAuthoring));
        var side = new SimulateSide(domain);
        side.Create("Item", ("Price", 0.25m));
        side.EvaluatePolicy("Cheap");
        var outcome = side.Outcomes[1];
        return outcome.Success ? $"success=True; answer={outcome.State["Cheap"]}" : $"success=False; message={outcome.Message}";
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
    /// Pinned as each host behaves today, so T-18 is shown not to change it. Subtraction lowers to
    /// <c>AddDays((Int32)-Days)</c>: the interpreter throws on it while the printed C# runs. That is a known-wrong
    /// result, pinned as it is and not as it should be: the VM's decimal negation (issue 832, row T-21) gives -1 for
    /// <c>-2m</c> and throws on <c>(int)-2m</c>.
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
    /// A known-wrong result: the domain analyzer accepts a form neither host runs (temporal offsets, row T-10).
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

    static string OffsetPlan(string due, string expression, string parameters = "", bool linked = false) => $$"""
        domain D
        uses temporal
        {{(linked ? "Item: entity {\n  Start: Date\n}\n" : "")}}Plan: entity {
          Days: Decimal
          Due: {{due}}
          {{(linked ? "item: Item\n  " : "")}}Start: Date
          Shift: action {{parameters}}{ assign Due to {{expression}} }
        }
        """;

    /// <summary>
    /// Review F1: the domain lowering turns a Date ± Decimal into <c>AddDays</c> only when the left is a date-typed
    /// property or parameter; any other left reaches the lowered analysis as a Date-with-Decimal operator node. On
    /// master those shapes passed analysis and failed on both hosts: the interpreter threw at run time
    /// (<c>InvalidCastException</c> from the <c>Convert.ToDecimal</c> rewrite) and the printed C# did not compile (CS0019).
    /// T-18 rejects them at analysis (the provider defines no such operator), so the interpreter's compile fails
    /// with that message and the printed C# still fails with CS0019.
    /// </summary>
    [Test]
    [Arguments("DateTime", "Now + Days", false, "'DateTime' and 'Decimal'): no Add operator", "'+' cannot be applied to operands of type 'DateTime' and 'decimal'")]
    [Arguments("DateTime", "Now - Days", false, "'DateTime' and 'Decimal'): no Subtract operator", "'-' cannot be applied to operands of type 'DateTime' and 'decimal'")]
    [Arguments("Date", "Today + Days", false, "'DateOnly' and 'Decimal'): no Add operator", "'+' cannot be applied to operands of type 'DateOnly' and 'decimal'")]
    [Arguments("Date", "Today - Days", false, "'DateOnly' and 'Decimal'): no Subtract operator", "'-' cannot be applied to operands of type 'DateOnly' and 'decimal'")]
    [Arguments("Date", "(Start + 1) + Days", false, "'DateOnly' and 'Decimal'): no Add operator", "'+' cannot be applied to operands of type 'DateOnly' and 'decimal'")]
    [Arguments("Date", "(Start + 1) - Days", false, "'DateOnly' and 'Decimal'): no Subtract operator", "'-' cannot be applied to operands of type 'DateOnly' and 'decimal'")]
    [Arguments("Date", "item Start + Days", true, "'DateOnly' and 'Decimal'): no Add operator", "'+' cannot be applied to operands of type 'DateOnly' and 'decimal'")]
    [Arguments("Date", "item Start - Days", true, "'DateOnly' and 'Decimal'): no Subtract operator", "'-' cannot be applied to operands of type 'DateOnly' and 'decimal'")]
    public async Task DateWithDecimalOperatorNode_IsRejectedByTheLoweredAnalysis_AndThePrintedHostDoesNotCompile(
        string due, string expression, bool linked, string analysis, string printed) {
        var dsl = OffsetPlan(due, expression, linked: linked);
        var domain = EvolvedDomain.FromDsl(dsl).Domain;
        RuntimeAnalysisCache.Bind(domain, ParitySessions.Create(domain.Extensions.Count > 0 ? domain.Extensions : ExtensionCatalog.ProductAuthoring));
        var side = new SimulateSide(domain);
        object dueValue = due == "Date" ? new DateOnly(2000, 1, 1) : new DateTime(2000, 1, 1);
        if (linked) {
            var item = side.Create("Item", ("Start", new DateOnly(2024, 2, 28)));
            side.Create("Plan", ("Days", 2m), ("Due", dueValue), ("item", item), ("Start", new DateOnly(2024, 2, 28)));
        }
        else {
            side.Create("Plan", ("Days", 2m), ("Due", dueValue), ("Start", new DateOnly(2024, 2, 28)));
        }
        side.Invoke("Shift");
        var shift = side.Outcomes[^1];
        await Assert.That(shift.Success).IsFalse();
        await Assert.That(shift.ExceptionType).IsEqualTo("InvalidOperationException");
        await Assert.That(shift.Message).IsEqualTo(
            "VM compile rejected: AST member body analysis reported errors: arithmetic operand is not numeric (got " + analysis + " for these types");
        await Assert.That(() => ParityScenario.FromDsl(dsl, "T18F1" + (linked ? "Linked" : "") + expression.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("+", "Plus").Replace("-", "Minus")))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("error CS0019: Operator " + printed);
    }

    /// <summary>The accepting sibling: a date action parameter is lowered to <c>AddDays</c> and both hosts agree.</summary>
    [Test]
    public async Task DateParameterPlusDecimal_IsLoweredToAddDays_BothHostsAgree() {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try {
            var outcomes = await ParityScenario.FromDsl(OffsetPlan("Date", "start + Days", parameters: "(start: Date) "), "T18F1DateParam").AssertAgree(side => {
                side.Create("Plan", ("Days", 2m), ("Due", new DateOnly(2000, 1, 1)), ("Start", new DateOnly(2000, 1, 1)));
                side.Invoke("Shift", ("start", new DateOnly(2024, 2, 28)));
            });
            await Assert.That(outcomes[^1].Success).IsTrue();
            await Assert.That(outcomes[^1].State["Due"]).IsEqualTo("03/01/2024");
        }
        finally {
            CultureInfo.CurrentCulture = culture;
        }
    }

    const string OptionalFlag = """
        domain Opt
        Task: entity {
          Done: Boolean
          Flag: Boolean optional
          Out: Boolean optional
          Ok: policy { Done and Flag }
          Copy: action { assign Out to Done and Flag }
          Check: action require Ok { assign Out to true }
        }
        """;

    /// <summary>
    /// Issue 862: an optional Boolean beside <c>and</c> is read as <c>Flag == true</c> (the owner's ruling), so a stored false
    /// blocks the guard and absent is not true. Before T-21a the interpreter read the optional's presence and a stored false
    /// let <c>Check</c> through. The printed C# does not compile (<c>&amp;&amp;</c> on bool and bool?, CS0019) until row T-06
    /// lowers the explicit <c>== true</c>, so this runs on the interpreter only.
    /// </summary>
    [Test]
    [Arguments(true, "Copy: success=True; Out=True | Check: success=True; guard=")]
    [Arguments(false, "Copy: success=True; Out=False | Check: success=False; guard=Ok")]
    [Arguments(null, "Copy: success=True; Out=False | Check: success=False; guard=Ok")]
    public async Task OptionalBooleanBesideAnd_IsReadAsEqualsTrue_Interpreter(bool? flag, string expected) {
        var domain = EvolvedDomain.FromDsl(OptionalFlag).Domain;
        RuntimeAnalysisCache.Bind(domain, ParitySessions.Create(domain.Extensions.Count > 0 ? domain.Extensions : ExtensionCatalog.ProductAuthoring));
        var side = new SimulateSide(domain);
        side.Create("Task", flag is null ? [("Done", true)] : [("Done", true), ("Flag", flag)]);
        side.Invoke("Copy");
        side.Invoke("Check");
        var (copy, check) = (side.Outcomes[1], side.Outcomes[2]);
        await Assert.That($"Copy: success={copy.Success}; Out={copy.State["Out"]} | Check: success={check.Success}; guard={check.FailureGuard}")
            .IsEqualTo(expected);
    }

    /// <summary>A step that threw reports no state (the harness default), so its Due is named as not read.</summary>
    static string Describe(ParityOutcome outcome) =>
        $"success={outcome.Success}; exception={outcome.ExceptionType ?? "none"}; message={outcome.Message}; Due={(outcome.ExceptionType is null ? outcome.State["Due"] : "<state not read>")}";
}