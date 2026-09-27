using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// One .poly model with <c>when any</c>: simulate runs the module handler
/// session.Lower produced, and printed C# is that same body.
/// </summary>
public class WhenAnySimulatePrintAgreeTests {
    [Test]
    public async Task WhenAny_SimulateAndPrintedCsharp_ShareTheModuleHandler() {
        var poly = """
            domain Watch
            Loan: entity {
              Code: Text
              Draft: stage {
                Overdue: action { transition to Overdue }
              }
              Overdue: stage { }
            }
            Patron: entity {
              Flag: Text
              loans: many Loan
              when any loans Overdue {
                assign Flag to "FIRED"
              }
            }
            """;
        var (domain, analysis, session) = Evolve(poly);
        var module = session.Lower(domain, analysis);
        var patronType = module.First(t => t.Name == "Patron");
        var handler = patronType.Methods?.FirstOrDefault(m => m.Name == "WhenAnyLoanOverdue");
        await Assert.That(handler).IsNotNull();
        await Assert.That(handler!.Body).IsNotNull();
        await Assert.That(ContainsNode<ThisReference>(handler.Body!)).IsTrue();

        var pending = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");
        var plan = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(pending)
            ?? throw new InvalidOperationException("missing entity subscription plan");
        var entry = plan.ByRelationshipName.Values.SelectMany(e => e).First();
        await Assert.That(RuntimeAnalysisCache.TryGetSubscriptionBody(domain, entry, "Overdue", out var cached))
            .IsTrue();
        await Assert.That(ReferenceEquals(handler.Body, cached)).IsTrue();

        var printed = new CSharpGenerator().Generate(handler.Body!);
        await Assert.That(printed.Contains("linkedMatched", StringComparison.Ordinal)).IsTrue();
        var files = session.Emit(domain, analysis);
        var patronCs = files.First(f => f.FileName == "Patron.cs").Source;
        await Assert.That(patronCs.Contains("WhenAnyLoanOverdue", StringComparison.Ordinal)).IsTrue();
        await Assert.That(patronCs.Contains("linkedMatched != 1L", StringComparison.Ordinal)
            || patronCs.Contains("linkedMatched != 1", StringComparison.Ordinal)).IsTrue();

        var loanE = domain.Types.OfType<Entity>().First(e => e.Name == "Loan");
        var patronE = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");
        var store = new DomainInstanceStore();
        var loan1 = DomainEntityInstance.Create(loanE,
            new Dictionary<string, object?> { ["Code"] = "L1" }, domain);
        var loan2 = DomainEntityInstance.Create(loanE,
            new Dictionary<string, object?> { ["Code"] = "L2" }, domain);
        var patron = DomainEntityInstance.Create(patronE,
            new Dictionary<string, object?> { ["Flag"] = "NONE" }, domain);
        store.Add(loan1);
        store.Add(loan2);
        store.Add(patron);
        store.Link("loans", patron, loan1);
        store.Link("loans", patron, loan2);

        await Assert.That(loan1.InvokeAction("Overdue").Succeeded).IsTrue();
        await Assert.That(patron.GetProperty<string>("Flag")).IsEqualTo("FIRED");

        patron.SetProperty("Flag", "NONE");
        await Assert.That(loan2.InvokeAction("Overdue").Succeeded).IsTrue();
        await Assert.That(patron.GetProperty<string>("Flag")).IsEqualTo("NONE");
    }

    private static bool ContainsNode<T>(Node node) where T : Node {
        if (node is T)
            return true;
        foreach (var child in node.Children) {
            if (child is not null && ContainsNode<T>(child))
                return true;
        }
        return false;
    }

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) Evolve(string poly) {
        var session = DomainSession.ForSource(poly, ExtensionCatalog.ProductAuthoring);
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, result.Analysis, session);
    }
}
