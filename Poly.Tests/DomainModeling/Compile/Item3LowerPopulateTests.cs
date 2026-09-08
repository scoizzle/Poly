using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// Item 3: policies, OnEntry/OnExit batches, and subscription handlers are
/// populated at session.Lower / GetOrLower — Domain-bound hot path binds those trees.
/// </summary>
public class Item3LowerPopulateTests {
    [Test]
    public async Task Policy_IsOnModule_AndEvaluatePolicy_UsesIt() {
        var (domain, analysis, session) = Evolve("""
            domain Guards
            Gate: entity {
              Age: Number required
              IsAdult: policy { Age >= 18 }
            }
            """);
        var module = session.Lower(domain, analysis);
        var gateType = module.First(t => t.Name == "Gate");
        await Assert.That(gateType.Methods?.Any(m => m.Name == "IsAdult" && m.Body is not null)).IsTrue();
        await Assert.That(RuntimeAnalysisCache.TryGetModuleMethod(domain, "Gate", "IsAdult", out var method)).IsTrue();
        await Assert.That(method?.Body).IsNotNull();

        var gateE = domain.Types.OfType<Entity>().First(e => e.Name == "Gate");
        var adult = DomainEntityInstance.Create(gateE,
            new Dictionary<string, object?> { ["Age"] = 21L }, domain);
        var minor = DomainEntityInstance.Create(gateE,
            new Dictionary<string, object?> { ["Age"] = 16L }, domain);
        var policy = gateE.Policies.First(p => p.Name == "IsAdult");
        await Assert.That(adult.EvaluatePolicy(policy)).IsTrue();
        await Assert.That(minor.EvaluatePolicy(policy)).IsFalse();
    }

    [Test]
    public async Task OnExit_IsPopulated_AndTransitionStage_AppliesAssign() {
        var (domain, analysis, session) = Evolve("""
            domain Flow
            Ticket: entity {
              Note: Text
              Advance: action { transition to Done }
              Draft: stage {
                exit { assign Note to "left-draft" }
              }
              Done: stage { }
            }
            """);
        session.Lower(domain, analysis);
        await Assert.That(RuntimeAnalysisCache.TryGetExitMethod(domain, "Ticket", "Draft", out var exit)).IsTrue();
        await Assert.That(exit?.Body).IsNotNull();

        var ticketE = domain.Types.OfType<Entity>().First(e => e.Name == "Ticket");
        var ticket = DomainEntityInstance.Create(ticketE,
            new Dictionary<string, object?> { ["Note"] = "" }, domain);
        await Assert.That(ticket.CurrentStage).IsEqualTo("Draft");
        var result = ticket.InvokeAction("Advance");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(ticket.CurrentStage).IsEqualTo("Done");
        await Assert.That(ticket.GetProperty<string>("Note")).IsEqualTo("left-draft");
    }

    [Test]
    public async Task Subscription_FiresFromCachedHandler_OnPeerTransition() {
        var (domain, analysis, session) = Evolve("""
            domain Watch
            Order: entity {
              Open: stage { }
              Active: stage { }
              Activate: action { transition to Active }
            }
            Tracker: entity {
              Status: Text
              Tracks: Order
              Pending: stage {
                when Tracks Active {
                  assign Status to "Triggered"
                }
              }
            }
            """);
        session.Lower(domain, analysis);

        var orderE = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var trackerE = domain.Types.OfType<Entity>().First(e => e.Name == "Tracker");
        var store = new DomainInstanceStore();
        var order = DomainEntityInstance.Create(orderE, domain: domain);
        var tracker = DomainEntityInstance.Create(trackerE,
            new Dictionary<string, object?> { ["Status"] = "Idle" }, domain);
        store.Add(order);
        store.Add(tracker);
        store.Link("Tracks", tracker, order);

        await Assert.That(tracker.CurrentStage).IsEqualTo("Pending");
        await Assert.That(order.CurrentStage).IsEqualTo("Open");

        var activated = order.InvokeAction("Activate");
        await Assert.That(activated.Succeeded).IsTrue();
        await Assert.That(order.CurrentStage).IsEqualTo("Active");
        await Assert.That(tracker.GetProperty<string>("Status")).IsEqualTo("Triggered");
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
