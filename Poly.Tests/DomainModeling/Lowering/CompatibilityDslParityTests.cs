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

    static string OffsetPlan(string due, string expression, string parameters = "", bool linked = false, string start = "Date") => $$"""
        domain D
        uses temporal
        {{(linked ? "Item: entity {\n  Start: Date\n}\n" : "")}}Plan: entity {
          Days: Decimal
          Due: {{due}}
          {{(linked ? "item: Item\n  " : "")}}Start: {{start}}
          Shift: action {{parameters}}{ assign Due to {{expression}} }
        }
        """;

    /// <summary>
    /// Owner ruling 2026-11-13 (T-10a): every Decimal day count is refused at analysis. This replaces the pins of what ran or
    /// failed before. On master a Date property or parameter ± a Decimal ran on both hosts and silently dropped the fraction
    /// (`(Int32)2.5m` is 2; subtraction threw in the interpreter); a DateTime ± a Decimal failed on both hosts; any other
    /// left (<c>Now</c>, <c>Today</c>, a parenthesised date, a path) reached the lowered analysis as a Date-with-Decimal
    /// operator node, which the interpreter refused and the printed C# did not compile (T-18 review F1).
    /// </summary>
    [Test]
    [Arguments("DateTime", "Now + Days", false)]
    [Arguments("DateTime", "Now - Days", false)]
    [Arguments("Date", "Today + Days", false)]
    [Arguments("Date", "Today - Days", false)]
    [Arguments("Date", "(Start + 1) + Days", false)]
    [Arguments("Date", "(Start + 1) - Days", false)]
    [Arguments("Date", "item Start + Days", true)]
    [Arguments("Date", "item Start - Days", true)]
    [Arguments("Date", "Start + Days", false)]
    [Arguments("Date", "Start - Days", false)]
    [Arguments("Date", "Start + 1.5", false)]
    [Arguments("DateTime", "Start + Days", false)]
    [Arguments("DateTime", "Start - Days", false)]
    public async Task DateWithDecimalCount_IsRefusedAtAnalysis(string due, string expression, bool linked) {
        var dsl = OffsetPlan(due, expression, linked: linked, start: due == "DateTime" && expression.StartsWith("Start", StringComparison.Ordinal) ? "DateTime" : "Date");

        await Assert.That(() => EvolvedDomain.FromDsl(dsl))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("a Decimal day count is refused");
    }

    /// <summary>A date parameter plus a Decimal parameter ran on both hosts on master; it is refused now.</summary>
    [Test]
    public async Task DateParameterPlusDecimal_IsRefusedAtAnalysis() {
        await Assert.That(() => EvolvedDomain.FromDsl(OffsetPlan("Date", "start + Days", parameters: "(start: Date) ")))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("a Decimal day count is refused");
    }
}