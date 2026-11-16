using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling;

/// <summary>
/// p4 suite goldens: DSL-authored <c>when any|all Rel Stage</c> subscriptions fire
/// through the lowered handler session.Lower produces (the same body print emits).
/// The default Each path (no keyword) stays per-element. These prove Any/All live
/// in that shared tree, not in a store-side gate.
/// </summary>
public class P4SubscriptionQuantifierDslTests {
    private static (Domain Domain, AnalysisResult Analysis) ParseAndAnalyze(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        if (!result.Succeeded) {
            var errors = string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new InvalidOperationException($"Evolution failed: {errors}");
        }
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        return (result.Root!, analysis);
    }

    private static (ModuleInstance Patron, ModuleInstance[] Loans, ModuleInstanceExecutor Store)
        BuildPatronLoanHarness(Domain domain, string patronFlag) {
        var exec = ModuleExec.For(domain);
        var patronEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Patron");
        var loanEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Loan");
        var patron = exec.Create((patronEntity).Name, new Dictionary<string, object?> { ["Flag"] = patronFlag }).Instance!;
        var loan1 = exec.Create((loanEntity).Name, new Dictionary<string, object?> { ["Code"] = "L1" }).Instance!;
        var loan2 = exec.Create((loanEntity).Name, new Dictionary<string, object?> { ["Code"] = "L2" }).Instance!;
        exec.Link("loans", patron, loan1);
        exec.Link("loans", patron, loan2);
        return (patron, [loan1, loan2], exec);
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
              patron: Patron via loans optional
              Draft: stage {
                Overdue: action { transition to Overdue }
              }
              Overdue: stage {}
            }
            """);
        var (patron, loans, store) = BuildPatronLoanHarness(domain, "NONE");

        // First loan overdue → Any fires once.
        await Assert.That(store.Invoke(loans[0], "Overdue").Succeeded).IsTrue();
        await Assert.That(patron.Bag["Flag"]).IsEqualTo("FIRED");

        // Second loan overdue (set state still matches) → Any fires again on this
        // notify, but once per transition (never per-linked-target).
        patron.Bag["Flag"] = "NONE";
        await Assert.That(store.Invoke(loans[1], "Overdue").Succeeded).IsTrue();
        await Assert.That(patron.Bag["Flag"]).IsEqualTo("FIRED");
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
              patron: Patron via loans optional
              Draft: stage {
                Overdue: action { transition to Overdue }
              }
              Overdue: stage {}
            }
            """);
        var (patron, loans, store) = BuildPatronLoanHarness(domain, "NONE");

        // Only one of two loans overdue → All must NOT fire.
        await Assert.That(store.Invoke(loans[0], "Overdue").Succeeded).IsTrue();
        await Assert.That(patron.Bag["Flag"]).IsEqualTo("NONE");

        // Second loan overdue → every linked target now matches → All fires once.
        await Assert.That(store.Invoke(loans[1], "Overdue").Succeeded).IsTrue();
        await Assert.That(patron.Bag["Flag"]).IsEqualTo("ALL-FIRED");
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
              patron: Patron via loans optional
              Draft: stage {
                Overdue: action { transition to Overdue }
              }
              Overdue: stage {}
            }
            """);
        var (patron, loans, store) = BuildPatronLoanHarness(domain, "NONE");

        // Each: fires per transition; peer = transitioned instance.
        await Assert.That(store.Invoke(loans[0], "Overdue").Succeeded).IsTrue();
        await Assert.That(patron.Bag["Flag"]).IsEqualTo("L1");

        await Assert.That(store.Invoke(loans[1], "Overdue").Succeeded).IsTrue();
        await Assert.That(patron.Bag["Flag"]).IsEqualTo("L2");
    }
}