using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// Issue #406 slice 2: a declared function is callable from a domain expression, end to end
/// (parse, analyze, evaluate, print, round-trip). Recursion is rejected (DMFUN004), as the issue
/// specifies ("no recursion in v1").
/// </summary>
public sealed class FunctionCallExpressionTests {
    private const string Cart = """
        domain Shop

        Cart: entity {
          Total: Number default(0)
          Label: Text default("x")
          Compute: action { assign Total to Add(1, 2) }
          Bump: action { assign Total to Add(Total, 5) }
          Nested: action { assign Total to Square(Add(1, 2)) }
          Scaled: action { assign Total to Add(1, 2) * 10 }
          Chained: action { assign Total to Add(Add(1, 2), Add(3, 4)) }
          IsThree: policy { Total is Add(1, 2) }
          IsThirty: policy { Total is Add(1, 2) * 10 }
        }

        function Add(a: Number, b: Number): Number = a + b
        function Square(a: Number): Number = a * a
        """;

    private static EvolutionResult Apply(string poly) =>
        new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(poly).Parse());

    private static string Diagnostic(string poly, string code) {
        var result = Apply(poly);
        var found = result.Analysis.Diagnostics.FirstOrDefault(d =>
            d.Code == code && d.Severity == Poly.Analysis.DiagnosticSeverity.Error);
        return found?.Message ?? "";
    }

    private static string WithFunctions(string functions, string action = "assign Total to Add(1, 2)") => $$"""
        domain Shop

        Cart: entity {
          Total: Number default(0)
          Label: Text default("x")
          Compute: action { {{action}} }
        }

        {{functions}}
        """;

    private const string AddFn = "function Add(a: Number, b: Number): Number = a + b";

    // ── parse + analyze ─────────────────────────────────────────

    [Test]
    public async Task Parse_Call_ProducesFunctionCallNodeWithArguments() {
        var result = Apply(Cart);
        await Assert.That(result.Succeeded).IsTrue();
        var policy = result.Root!.Types.OfType<Entity>().Single().Policies.Single(p => p.Name == "IsThree");
        var cmp = (Comparison)policy.Expression;
        var call = (FunctionCall)cmp.Right;
        await Assert.That(call.Name).IsEqualTo("Add");
        await Assert.That(call.Arguments.Count).IsEqualTo(2);
        await Assert.That(call.Line).IsGreaterThan(0);
    }

    [Test]
    public async Task Parse_BareIdentifierSameAsFunctionName_StaysPropertyAccess() {
        var result = Apply("""
            domain Shop
            Cart: entity {
              Add: Number default(0)
              IsZero: policy { Add is 0 }
            }
            function Add(a: Number, b: Number): Number = a + b
            """);
        await Assert.That(result.Succeeded).IsTrue();
        var policy = result.Root!.Types.OfType<Entity>().Single().Policies.Single();
        await Assert.That(((Comparison)policy.Expression).Left).IsTypeOf<PropertyAccess>();
    }

    [Test]
    public async Task Parse_ZeroArgumentCall_Parses() {
        var result = Apply("""
            domain Shop
            Cart: entity {
              Total: Number default(0)
              Reset: action { assign Total to Seven() }
            }
            function Seven(): Number = 7
            """);
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Analyze_WholeCartDomain_IsClean() {
        var result = Apply(Cart);
        await Assert.That(result.Succeeded).IsTrue();
    }

    // ── round trip ──────────────────────────────────────────────

    [Test]
    public async Task WriteRead_DocumentWithCalls_IsIdempotent() {
        var first = new DomainDslPrinter().Print(Apply(Cart).Root!);
        await Assert.That(first).Contains("Add(1, 2)");
        await Assert.That(first).Contains("Square(Add(1, 2))");
        var second = new DomainDslPrinter().Print(Apply(first).Root!);
        await Assert.That(second).IsEqualTo(first);
    }

    [Test]
    public async Task WriteRead_CallInsideFunctionBody_IsIdempotent() {
        var poly = WithFunctions(AddFn + "\nfunction Add3(a: Number, b: Number, c: Number): Number = Add(Add(a, b), c)");
        var first = new DomainDslPrinter().Print(Apply(poly).Root!);
        await Assert.That(first).Contains("Add(Add(a, b), c)");
        var second = new DomainDslPrinter().Print(Apply(first).Root!);
        await Assert.That(second).IsEqualTo(first);
    }

    // ── fail closed ─────────────────────────────────────────────

    [Test]
    public async Task Analyze_UnknownFunction_ReportsDMFUN001() {
        var message = Diagnostic(WithFunctions(AddFn, "assign Total to Nope(1, 2)"), "DMFUN001");
        await Assert.That(message).Contains("Nope");
    }

    [Test]
    public async Task Analyze_TooFewArguments_ReportsDMFUN002() {
        var message = Diagnostic(WithFunctions(AddFn, "assign Total to Add(1)"), "DMFUN002");
        await Assert.That(message).Contains("expects 2");
    }

    [Test]
    public async Task Analyze_TooManyArguments_ReportsDMFUN002() {
        var message = Diagnostic(WithFunctions(AddFn, "assign Total to Add(1, 2, 3)"), "DMFUN002");
        await Assert.That(message).Contains("got 3");
    }

    [Test]
    public async Task Analyze_WrongArgumentType_ReportsDMFUN003() {
        var message = Diagnostic(WithFunctions(AddFn, "assign Total to Add(Label, 2)"), "DMFUN003");
        await Assert.That(message).Contains("'a'");
    }

    [Test]
    public async Task Analyze_CallResultAssignedToWrongType_ReportsTypeCompatibility() {
        var message = Diagnostic(WithFunctions(AddFn, "assign Label to Add(1, 2)"), "DMSEM003");
        await Assert.That(message).IsNotEmpty();
    }

    [Test]
    public async Task Analyze_DirectRecursion_ReportsDMFUN004() {
        var message = Diagnostic(
            WithFunctions("function Loop(a: Number): Number = Loop(a)", "assign Total to Loop(1)"), "DMFUN004");
        await Assert.That(message).Contains("Loop");
    }

    [Test]
    public async Task Analyze_MutualRecursion_ReportsDMFUN004() {
        var message = Diagnostic(
            WithFunctions("""
                function Ping(a: Number): Number = Pong(a)
                function Pong(a: Number): Number = Ping(a)
                """, "assign Total to Ping(1)"), "DMFUN004");
        await Assert.That(message).IsNotEmpty();
    }

    [Test]
    public async Task Analyze_FunctionBodyReadsEntityState_ReportsDMFUN005() {
        var message = Diagnostic(
            WithFunctions("function Peek(a: Number): Number = Total + a", "assign Total to Peek(1)"), "DMFUN005");
        await Assert.That(message).Contains("Total");
    }

    [Test]
    public async Task Analyze_FunctionBodyTypeDiffersFromReturnType_ReportsDMFUN006() {
        var message = Diagnostic(
            WithFunctions("function Bad(a: Number): Text = a + 1"), "DMFUN006");
        await Assert.That(message).Contains("Bad");
    }

    [Test]
    public async Task Analyze_DuplicateFunctionName_ReportsDMFUN007() {
        var message = Diagnostic(WithFunctions(AddFn + "\n" + AddFn), "DMFUN007");
        await Assert.That(message).Contains("Add");
    }

    [Test]
    public async Task Analyze_CallInPropertyDefault_ReportsDMFUN008() {
        var message = Diagnostic("""
            domain Shop
            Cart: entity {
              Total: Number default(Add(1, 2))
            }
            function Add(a: Number, b: Number): Number = a + b
            """, "DMFUN008");
        await Assert.That(message).Contains("Add");
    }

    [Test]
    public async Task Parse_CallWithTrailingComma_IsPositionedParseError() {
        var ex = Assert.Throws<Poly.Grammar.GrammarException>(() => Apply(WithFunctions(AddFn, "assign Total to Add(1, 2,)")));
        await Assert.That(ex.Message).Contains("argument");
    }

    // ── evaluate on both sides (simulate + printed C#) ─────────

    [Test]
    public async Task Evaluate_CallInAssign_YieldsThreeOnBothSides() {
        var outcomes = await ParityScenario.FromDsl(Cart, "FnCallAssign").AssertAgree(side => {
            side.Create("Cart");
            side.Invoke("Compute");
        });
        await Assert.That(outcomes[1].Success).IsTrue();
        await Assert.That(outcomes[1].State["Total"]).IsEqualTo("3");
    }

    [Test]
    public async Task Evaluate_CallInLargerExpression_Scales() {
        var outcomes = await ParityScenario.FromDsl(Cart, "FnCallScaled").AssertAgree(side => {
            side.Create("Cart");
            side.Invoke("Scaled");
        });
        await Assert.That(outcomes[1].State["Total"]).IsEqualTo("30");
    }

    [Test]
    public async Task Evaluate_CallAsArgumentOfCall_Nests() {
        var outcomes = await ParityScenario.FromDsl(Cart, "FnCallNested").AssertAgree(side => {
            side.Create("Cart");
            side.Invoke("Nested");
            side.Invoke("Chained");
        });
        await Assert.That(outcomes[1].State["Total"]).IsEqualTo("9");
        await Assert.That(outcomes[2].State["Total"]).IsEqualTo("10");
    }

    [Test]
    public async Task Evaluate_PropertyAsArgument_ReadsCallerState() {
        var outcomes = await ParityScenario.FromDsl(Cart, "FnCallPropertyArg").AssertAgree(side => {
            side.Create("Cart");
            side.Invoke("Bump");
            side.Invoke("Bump");
        });
        await Assert.That(outcomes[2].State["Total"]).IsEqualTo("10");
    }

    /// <summary>
    /// G-10 (reviewer F2): a <c>match</c> with commas in its arms is now a legal call argument (master cut the argument at the
    /// first comma inside the braces, by accident; a comma-free <c>match { else -> 1 }</c> argument already parsed). End to end: analysed, run on the
    /// simulator and on the printed C#, and the two agree. The fail-closed neighbours (mixed Decimal and whole-number arms) are
    /// <c>CalleeFrameIsolationTests.DecimalMatch_*</c>, which cover the arm shapes the call argument shares.
    /// </summary>
    [Test]
    public async Task Evaluate_MatchWithCommasAsCallArgument_AgreesOnBothSides() {
        const string poly = """
            domain Shop

            Cart: entity {
              Total: Number default(0)
              Qty: Number default(0)
              Compute: action { assign Total to Add(match { Qty > 3 -> 10, Qty > 1 -> 5, else -> 1 }, 1) }
            }

            function Add(a: Number, b: Number): Number = a + b
            """;
        await Assert.That(Apply(poly).Succeeded).IsTrue();

        var outcomes = await ParityScenario.FromDsl(poly, "FnCallMatchArgument").AssertAgree(side => {
            side.Create("Cart", ("Qty", 5L));
            side.Invoke("Compute");
            side.Create("Cart", ("Qty", 2L));
            side.Invoke("Compute");
            side.Create("Cart", ("Qty", 0L));
            side.Invoke("Compute");
        });
        await Assert.That(outcomes[1].State["Total"]).IsEqualTo("11");
        await Assert.That(outcomes[3].State["Total"]).IsEqualTo("6");
        await Assert.That(outcomes[5].State["Total"]).IsEqualTo("2");
    }

    [Test]
    public async Task Evaluate_CallInPolicy_AnswersOnBothSides() {
        var outcomes = await ParityScenario.FromDsl(Cart, "FnCallPolicy").AssertAgree(side => {
            side.Create("Cart");
            side.EvaluatePolicy("IsThree");
            side.Invoke("Compute");
            side.EvaluatePolicy("IsThree");
            side.Invoke("Scaled");
            side.EvaluatePolicy("IsThirty");
        });
        await Assert.That(outcomes[1].State["IsThree"]).IsEqualTo("False");
        await Assert.That(outcomes[3].State["IsThree"]).IsEqualTo("True");
        await Assert.That(outcomes[5].State["IsThirty"]).IsEqualTo("True");
    }
}