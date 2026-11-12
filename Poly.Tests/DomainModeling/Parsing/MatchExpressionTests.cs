using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Grammar;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// #409 slices 2-3: the expression-level <c>match { cond -> value, ..., else -> value }</c>.
/// A guard chain (no scrutinee); <c>else</c> is required; arms are expressions. It parses, prints,
/// round-trips, analyzes (DMMAT001-003), lowers to a nested conditional, and simulation agrees with printed C#.
/// </summary>
public class MatchExpressionTests {
    private const string Dsl = """
        domain Shop
        Order: entity {
          Qty: Number
          Tier: Text default("none")
          Open: stage {
            Classify: action {
              assign Tier to match { Qty > 100 -> "big", Qty > 10 -> "mid", else -> "small" }
            }
          }
        }
        """;

    private static string Wrap(string assignRhs, string extraProps = "") => $$"""
        domain Shop
        Order: entity {
          Qty: Number
          Tier: Text default("none")
          Flag: Boolean default(false)
          {{extraProps}}
          Open: stage {
            Classify: action {
              assign Tier to {{assignRhs}}
            }
          }
        }
        """;

    private static Domain Apply(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary);
        return result.Root;
    }

    private static AnalysisResult Analyze(string poly) => DomainModelAnalyzer.Analyze(Apply(poly));

    private static string ParseError(string poly) {
        GrammarException? caught = null;
        try { new PolyDslParser(poly).Parse(); }
        catch (GrammarException ex) { caught = ex; }
        if (caught is null) throw new InvalidOperationException("expected a parse error");
        if (caught.Line <= 0) throw new InvalidOperationException("parse error is not positioned: " + caught.Detail);
        return caught.Detail;
    }

    [Test]
    public async Task Match_PrintsAndRoundTrips() {
        var printer = new DomainDslPrinter();
        var first = printer.Print(Apply(Dsl));
        var second = printer.Print(Apply(first));

        await Assert.That(first).Contains("match {");
        await Assert.That(first).Contains("else ->");
        await Assert.That(second).IsEqualTo(first);
    }

    [Test]
    public async Task Match_Parses_IntoMatchExpr_WithArmsAndElse() {
        var changes = new PolyDslParser(Dsl).Parse();
        var assign = changes.OfType<AddEffectToActionChange>().Select(c => c.Effect)
            .OfType<Poly.DomainModeling.Ontology.Effects.AssignEffect>().Single();
        var match = (MatchExpr)assign.Value;

        await Assert.That(match.Arms.Count).IsEqualTo(2);
        await Assert.That(match.Arms[0].Condition).IsTypeOf<Comparison>();
        await Assert.That(match.Else).IsTypeOf<Literal>();
    }

    [Test]
    public async Task Match_IsClean_Analysis() {
        var analysis = Analyze(Dsl);

        await Assert.That(analysis.HasErrors).IsFalse();
    }

    [Test]
    public async Task Match_NonBooleanCondition_IsDMMAT001() {
        var analysis = Analyze(Wrap("match { Qty -> \"a\", else -> \"b\" }"));

        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "DMMAT001")).IsTrue();
    }

    [Test]
    public async Task Match_ArmTypesThatDoNotUnify_IsDMMAT002() {
        var analysis = Analyze(Wrap("match { Qty > 1 -> \"a\", else -> 2 }"));

        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "DMMAT002")).IsTrue();
    }

    [Test]
    public async Task Match_DuplicateCondition_IsDMMAT003() {
        var analysis = Analyze(Wrap("match { Qty > 1 -> \"a\", Qty > 1 -> \"b\", else -> \"c\" }"));

        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "DMMAT003")).IsTrue();
    }

    [Test]
    public async Task Match_ResultTypeMismatchWithTarget_IsRejected() {
        var analysis = Analyze(Wrap("match { Qty > 1 -> 1, else -> 2 }"));

        await Assert.That(analysis.HasErrors).IsTrue();
    }

    [Test]
    public async Task Match_UnknownPropertyInCondition_IsRejected() {
        var analysis = Analyze(Wrap("match { Nope > 1 -> \"a\", else -> \"b\" }"));

        await Assert.That(analysis.HasErrors).IsTrue();
    }

    [Test]
    [Arguments("match { }", "else")]
    [Arguments("match { Qty > 1 -> \"a\" }", "'else'")]
    [Arguments("match { else -> \"b\", Qty > 1 -> \"a\" }", "'else' must be the last")]
    [Arguments("match { else -> \"a\", else -> \"b\" }", "more than one 'else'")]
    [Arguments("match { Qty > 1 \"a\", else -> \"b\" }", "'->'")]
    [Arguments("match { Qty > 1 -> , else -> \"b\" }", "value")]
    [Arguments("match { -> \"a\", else -> \"b\" }", "condition")]
    [Arguments("match { Qty > 1 -> \"a\", , else -> \"b\" }", "Expected an arm")]
    [Arguments("match { Qty > 1 -> \"a\", else -> \"b\", }", "Expected an arm")]
    public async Task Match_Malformed_IsPositionedParseError(string rhs, string expected) {
        var detail = ParseError(Wrap(rhs));

        await Assert.That(detail).Contains(expected);
    }

    [Test]
    public async Task Match_Executes_ToTheFirstMatchingArm() {
        var domain = Apply(Dsl);
        var exec = ModuleExec.For(domain);

        async Task Check(long qty, string expected) {
            var order = exec.Create("Order", new Dictionary<string, object?> { ["Qty"] = qty }).Instance!;
            var result = exec.Invoke(order, "Classify");
            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That((string)order.Bag["Tier"]!).IsEqualTo(expected);
        }

        await Check(500, "big");
        await Check(50, "mid");
        await Check(1, "small");
    }

    [Test]
    public async Task Match_SimulationAndGeneratedCSharp_Agree() {
        var domain = Apply(Dsl);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var exec = ModuleExec.For(domain);
        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var orderType = asm.GetType("Order")!;

        foreach (var qty in new long[] { 500, 101, 100, 50, 11, 10, 1, 0 }) {
            var order = exec.Create("Order", new Dictionary<string, object?> { ["Qty"] = qty }).Instance!;
            await Assert.That(exec.Invoke(order, "Classify").Succeeded).IsTrue();

            var printed = ExportedCSharp.CreateEntity(orderType, ("qty", qty));
            orderType.GetMethod("Classify", PrintedMembers.Any)!.Invoke(printed, null);
            var printedTier = orderType.GetProperty("Tier", PrintedMembers.Any)!.GetValue(printed);

            await Assert.That(printedTier).IsEqualTo(order.Bag["Tier"]);
        }
    }

    [Test]
    public async Task Match_UnresolvedName_IsDMMAT004() {
        var analysis = Analyze(Wrap("match { Nope > 1 -> \"a\", else -> \"b\" }"));

        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "DMMAT004")).IsTrue();
    }

    [Test]
    public async Task Match_Nested_RoundTripsAndExecutes() {
        var nested = Wrap("match { Qty > 10 -> match { Flag -> \"bigflag\", else -> \"big\" }, else -> \"small\" }");
        var printer = new DomainDslPrinter();
        var first = printer.Print(Apply(nested));

        await Assert.That(printer.Print(Apply(first))).IsEqualTo(first);
        await Assert.That(DomainModelAnalyzer.Analyze(Apply(nested)).HasErrors).IsFalse();

        var exec = ModuleExec.For(Apply(nested));
        var order = exec.Create("Order", new Dictionary<string, object?> { ["Qty"] = 50L, ["Flag"] = true }).Instance!;
        await Assert.That(exec.Invoke(order, "Classify").Succeeded).IsTrue();
        await Assert.That((string)order.Bag["Tier"]!).IsEqualTo("bigflag");
    }

    [Test]
    public async Task Match_InPolicyBody_RoundTripsAndAnalyzes() {
        const string poly = """
            domain Shop
            Order: entity {
              Qty: Number
              Flag: Boolean default(false)
              Big: policy { match { Flag -> Qty > 1, else -> Qty > 100 } }
            }
            """;
        var printer = new DomainDslPrinter();
        var first = printer.Print(Apply(poly));

        await Assert.That(first).Contains("match {");
        await Assert.That(printer.Print(Apply(first))).IsEqualTo(first);
        await Assert.That(DomainModelAnalyzer.Analyze(Apply(poly)).HasErrors).IsFalse();
    }

    [Test]
    public async Task Match_AsPropertyName_StillParsesAsIdentifier() {
        const string poly = """
            domain Shop
            Order: entity {
              match: Boolean default(false)
              Ok: policy { match }
            }
            """;

        await Assert.That(DomainModelAnalyzer.Analyze(Apply(poly)).HasErrors).IsFalse();
    }
}