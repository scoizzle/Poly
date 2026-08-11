using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Parsing;

namespace Poly.Tests.DomainModeling;

/// <summary>
/// p4 suite goldens: DSL-authored `when any|all Rel Stage` subscriptions dispatch
/// through the existing store runtime with set-state-after-transition semantics,
/// and the default Each path (no keyword) stays per-element. Zero runtime changes
/// — these prove the store already implements Any/All for DSL-authored plans.
/// </summary>
public class P4SubscriptionQuantifierDslTests {
    private static (Domain Domain, AnalysisResult Analysis) ParseAndAnalyze(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded) {
            var errors = string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new InvalidOperationException($"Evolution failed: {errors}");
        }
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        return (result.Root!, analysis);
    }

    private static (DomainEntityInstance Patron, DomainEntityInstance[] Loans, DomainInstanceStore Store)
        BuildPatronLoanHarness(Domain domain, string patronFlag) {
        var store = new DomainInstanceStore();
        var patronEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Patron");
        var loanEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Loan");
        var patron = DomainEntityInstance.Create(patronEntity,
            new Dictionary<string, object?> { ["Flag"] = patronFlag }, domain: domain);
        var loan1 = DomainEntityInstance.Create(loanEntity,
            new Dictionary<string, object?> { ["Code"] = "L1" }, domain: domain);
        var loan2 = DomainEntityInstance.Create(loanEntity,
            new Dictionary<string, object?> { ["Code"] = "L2" }, domain: domain);
        store.Add(patron);
        store.Add(loan1);
        store.Add(loan2);
        store.Link("loans", patron, loan1);
        store.Link("loans", patron, loan2);
        return (patron, [loan1, loan2], store);
    }

    private static Domain ParseOnly(string poly) {
        var (domain, analysis) = ParseAndAnalyze(poly);
        if (analysis.HasErrors)
            throw new InvalidOperationException("Analysis has errors");
        return domain;
    }

    // ── Any golden: fires once when ≥1 linked target in matching stage ──

    [Test]
    public async Task WhenAny_FiresOnceWhenAnyLinkedLoanIsOverdue() {
        var domain = ParseOnly("""
            domain AnyGolden
            Patron: entity {
              Flag: Text
              loans: many Loan
              when any loans Overdue {
                assign Flag to "FIRED"
              }
            }
            Loan: entity {
              Code: Text
              Draft: stage {
                Overdue: action { transition to Overdue }
              }
              Overdue: stage {}
            }
            """);
        var (patron, loans, store) = BuildPatronLoanHarness(domain, "NONE");

        // First loan overdue → Any fires once.
        await Assert.That(loans[0].InvokeAction("Overdue").Succeeded).IsTrue();
        await Assert.That(patron.GetProperty<string>("Flag")).IsEqualTo("FIRED");

        // Second loan overdue (set state still matches) → Any fires again on this
        // notify, but once per transition (never per-linked-target).
        patron.SetProperty("Flag", "NONE");
        await Assert.That(loans[1].InvokeAction("Overdue").Succeeded).IsTrue();
        await Assert.That(patron.GetProperty<string>("Flag")).IsEqualTo("FIRED");
    }

    // ── All golden: fires once when EVERY linked target in matching stage ──

    [Test]
    public async Task WhenAll_FiresOnlyWhenEveryLinkedLoanIsOverdue() {
        var domain = ParseOnly("""
            domain AllGolden
            Patron: entity {
              Flag: Text
              loans: many Loan
              when all loans Overdue {
                assign Flag to "ALL-FIRED"
              }
            }
            Loan: entity {
              Code: Text
              Draft: stage {
                Overdue: action { transition to Overdue }
              }
              Overdue: stage {}
            }
            """);
        var (patron, loans, store) = BuildPatronLoanHarness(domain, "NONE");

        // Only one of two loans overdue → All must NOT fire.
        await Assert.That(loans[0].InvokeAction("Overdue").Succeeded).IsTrue();
        await Assert.That(patron.GetProperty<string>("Flag")).IsEqualTo("NONE");

        // Second loan overdue → every linked target now matches → All fires once.
        await Assert.That(loans[1].InvokeAction("Overdue").Succeeded).IsTrue();
        await Assert.That(patron.GetProperty<string>("Flag")).IsEqualTo("ALL-FIRED");
    }

    // ── Each regression: no keyword stays per-element with peer ──

    [Test]
    public async Task WhenNoKeyword_Each_FiresPerTransitionWithPeer() {
        var domain = ParseOnly("""
            domain EachRegression
            Patron: entity {
              Flag: Text
              loans: many Loan
              when loans Overdue as loan {
                assign Flag to loan Code
              }
            }
            Loan: entity {
              Code: Text
              Draft: stage {
                Overdue: action { transition to Overdue }
              }
              Overdue: stage {}
            }
            """);
        var (patron, loans, store) = BuildPatronLoanHarness(domain, "NONE");

        // Each: fires per transition; peer = transitioned instance.
        await Assert.That(loans[0].InvokeAction("Overdue").Succeeded).IsTrue();
        await Assert.That(patron.GetProperty<string>("Flag")).IsEqualTo("L1");

        await Assert.That(loans[1].InvokeAction("Overdue").Succeeded).IsTrue();
        await Assert.That(patron.GetProperty<string>("Flag")).IsEqualTo("L2");
    }
}