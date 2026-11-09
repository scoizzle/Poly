using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #409 slice 5: effect legality of the general <c>for x in Rel [where cond] { effects }</c>.
/// The body may write only the owning entity's own state; an assign through a relationship
/// or the loop binder is another entity's state (DMEFF012, the same rule as actions and
/// <c>when</c>). The effect walkers descend into the body. Lowering stays unshipped, so
/// DMEFF016 is still reported, and the legality error must be visible beside it.
/// The parser writes only a bare property as an assign target, so a cross-entity write
/// is planted through the model API.
/// </summary>
public class GeneralForEffectLegalityTests {
    private const string Dsl = """
        domain Billing
        Customer: entity {
          Balance: Number
          Active: stage {
            Bump: action { assign Balance to Balance + 1 }
          }
        }
        Line: entity {
          Qty: Number
          Open: stage {
            Mark: action { assign Qty to Qty + 1 }
          }
        }
        Invoice: entity {
          Note: Text
          customer: Customer
          lines: many Line
          Open: stage {
            Touch: action (note: Text) {
              for line in lines where line Qty > 1 {
                assign Note to note
              }
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

    // Replaces the body of Invoice.Touch's `for` with API-built effects the parser cannot produce.
    private static Domain WithForBody(Domain domain, params Effect[] body) =>
        WithTouchEffects(domain, touch => touch with {
            Effects = touch.Effects.Select(e => e is ForEachEffect fe ? fe with { Body = body } : e).ToList()
        });

    private static Domain WithTouchEffects(Domain domain, Func<Poly.DomainModeling.Ontology.Action, Poly.DomainModeling.Ontology.Action> rewrite) {
        var types = domain.Types.Select(t => t is Entity { Name: "Invoice" } invoice
            ? invoice with {
                Stages = invoice.Stages.Select(s => s with {
                    Actions = s.Actions.Select(a => a.Name == "Touch" ? rewrite(a) : a).ToList()
                }).ToList()
            }
            : t).ToList();
        return domain with { Types = types };
    }

    private static readonly AssignEffect CrossEntityAssign = new(
        new RelationshipNavigation("customer", DomainExpression.Property("Balance")),
        DomainExpression.Literal(0L));

    private static readonly AssignEffect BinderAssign = new(
        new RelationshipNavigation("line", DomainExpression.Property("Qty")),
        DomainExpression.Literal(0L));

    private static IReadOnlyList<Diagnostic> Errors(Domain domain) =>
        DomainModelAnalyzer.Analyze(domain).Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

    private static IEnumerable<Diagnostic> CrossEntityErrors(Domain domain) =>
        Errors(domain).Where(d => d.Code == DomainModelDiagnosticCodes.EffectCrossEntityMutation);

    [Test]
    public async Task CrossEntityAssign_InForBody_IsPositionedError() {
        var domain = WithForBody(Apply(Dsl), CrossEntityAssign);

        var errors = CrossEntityErrors(domain).ToList();

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(errors[0].Node, CrossEntityAssign)).IsTrue();
        await Assert.That(errors[0].Message).Contains("customer");
    }

    [Test]
    public async Task CrossEntityAssign_NestedInConditionalInForBody_IsError() {
        var nested = new ConditionalEffect(DomainExpression.Literal(true), [CrossEntityAssign], null);
        var domain = WithForBody(Apply(Dsl), nested);

        await Assert.That(CrossEntityErrors(domain).Count()).IsEqualTo(1);
    }

    [Test]
    public async Task LoopBinderAssign_InForBody_IsError() {
        var domain = WithForBody(Apply(Dsl), BinderAssign);

        var errors = CrossEntityErrors(domain).ToList();

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(errors[0].Node, BinderAssign)).IsTrue();
        await Assert.That(errors[0].Message).Contains("loop binder 'line'");
    }

    [Test]
    public async Task OwnStateAssign_InForBody_PassesLegality() {
        var errors = Errors(Apply(Dsl));

        await Assert.That(errors.Any(d => d.Code == DomainModelDiagnosticCodes.EffectCrossEntityMutation)).IsFalse();
        await Assert.That(errors.All(d => d.Code == DomainModelDiagnosticCodes.EffectForNotLowerable)).IsTrue();
    }

    [Test]
    public async Task InvokeOnLoopBinder_InForBody_PassesLegality() {
        var invokeBinder = new InvokeActionEffect("Mark", [], "line");
        var domain = WithForBody(Apply(Dsl), invokeBinder);

        var errors = Errors(domain);

        await Assert.That(errors.All(d => d.Code == DomainModelDiagnosticCodes.EffectForNotLowerable)).IsTrue();
    }

    [Test]
    public async Task CrossEntityBody_ReportsLegalityAlongsideNotLowerable() {
        var domain = WithForBody(Apply(Dsl), CrossEntityAssign);

        var codes = Errors(domain).Select(d => d.Code).ToList();

        await Assert.That(codes).Contains(DomainModelDiagnosticCodes.EffectForNotLowerable);
        await Assert.That(codes).Contains(DomainModelDiagnosticCodes.EffectCrossEntityMutation);
    }

    // ── walkers see the body ─────────────────────────────────────

    [Test]
    public async Task ParameterUsedOnlyInForBody_IsNotReportedUnused() {
        var analysis = DomainModelAnalyzer.Analyze(Apply(Dsl));

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectUnusedParameter
            && d.Message.Contains("'note'", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task ForLoopOverUnwrittenRelationship_IsReported() {
        var analysis = DomainModelAnalyzer.Analyze(Apply(Dsl));

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.UnwrittenRelationship
            && d.Node is ForEachEffect
            && d.Message.Contains("'for' loop", StringComparison.Ordinal))).IsTrue();
    }
}