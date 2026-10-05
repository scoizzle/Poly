using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// M1: only the owning entity's named behavior mutates its state. Actions, and entry,
/// exit and when blocks (V4 = a), may assign their own entity's state. A mutation that
/// targets another entity is an Analyze Error (DMEFF012); create-in and cross-entity
/// <c>invoke Rel.Action</c> remain the way to change another entity.
/// </summary>
public class CrossEntityMutationAnalysisTests {
    // Invoice reads Customer; it may change Customer only by invoking Customer.Bump.
    private const string Dsl = """
        domain Billing
        Customer: entity {
          Balance: Number
          Active: stage {
            Bump: action { assign Balance to Balance + 1 }
          }
        }
        Line: entity {
          Sku: Text
          Open: stage {
            Post: action { transition to Posted }
          }
          Posted: stage { }
        }
        Invoice: entity {
          Note: Text
          customer: Customer
          lines: many Line
          Open: stage {
            entry { assign Note to "opened" }
            exit { assign Note to "closing" }
            Touch: action { assign Note to "touched" }
            AddLine: action { create in lines { Sku: "A" } }
            Charge: action { invoke customer.Bump }
            Close: action { transition to Closed }
            when lines Posted as line {
              assign Note to line Sku
            }
          }
          Closed: stage { }
        }
        """;

    private static Domain Parse(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded) {
            var errors = string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new InvalidOperationException($"Evolution failed: {errors}");
        }
        return result.Root!;
    }

    private static readonly AssignEffect CrossEntityAssign = new(
        new RelationshipNavigation("customer", DomainExpression.Property("Balance")),
        DomainExpression.Literal(0L));

    // Rewrites Invoice's Open stage so a test can plant an API-built effect the parser cannot produce.
    private static Domain WithInvoiceOpenStage(Domain domain, Func<Stage, Stage> rewrite) {
        var types = domain.Types.Select(t => t is Entity { Name: "Invoice" } e
            ? e with { Stages = e.Stages.Select(s => s.Name == "Open" ? rewrite(s) : s).ToList() }
            : t).ToList();
        return domain with { Types = types };
    }

    private static IEnumerable<Diagnostic> CrossEntityErrors(Domain domain) =>
        DomainModelAnalyzer.Analyze(domain).Diagnostics.Where(d =>
            d.Code == DomainModelDiagnosticCodes.EffectCrossEntityMutation &&
            d.Severity == DiagnosticSeverity.Error);

    // ── positive: own-entity mutation (V4 = a), create-in, cross-entity invoke ──

    [Test]
    public async Task OwnStateInActionEntryExitWhen_CreateIn_AndCrossEntityInvoke_HaveNoErrors() {
        var analysis = DomainModelAnalyzer.Analyze(Parse(Dsl));

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectCrossEntityMutation)).IsFalse();
        await Assert.That(analysis.HasErrors).IsFalse();
    }

    // ── negative: a mutation targeting another entity is an Error with a code ──

    [Test]
    public async Task CrossEntityAssign_InAction_IsError() {
        var domain = WithInvoiceOpenStage(Parse(Dsl), s => s with {
            Actions = s.Actions.Select(a => a.Name == "Touch" ? a with { Effects = [CrossEntityAssign] } : a).ToList()
        });

        var errors = CrossEntityErrors(domain).ToList();

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("customer");
        await Assert.That(errors[0].Message).Contains("Invoice");
    }

    [Test]
    public async Task CrossEntityAssign_NestedInConditional_IsError() {
        var nested = new ConditionalEffect(DomainExpression.Literal(true), [CrossEntityAssign], null);
        var domain = WithInvoiceOpenStage(Parse(Dsl), s => s with {
            Actions = s.Actions.Select(a => a.Name == "Touch" ? a with { Effects = [nested] } : a).ToList()
        });

        await Assert.That(CrossEntityErrors(domain).Count()).IsEqualTo(1);
    }

    [Test]
    public async Task CrossEntityAssign_InEntryAndExit_IsError() {
        var domain = WithInvoiceOpenStage(Parse(Dsl), s => s with {
            OnEntryEffects = [CrossEntityAssign],
            OnExitEffects = [CrossEntityAssign],
        });

        await Assert.That(CrossEntityErrors(domain).Count()).IsEqualTo(2);
    }

    [Test]
    public async Task CrossEntityAssign_InWhenHandler_IsError() {
        var domain = WithInvoiceOpenStage(Parse(Dsl), s => s with {
            Subscriptions = s.Subscriptions.Select(sub => sub with { Effects = [CrossEntityAssign] }).ToList()
        });

        await Assert.That(CrossEntityErrors(domain).Count()).IsEqualTo(1);
    }
}
