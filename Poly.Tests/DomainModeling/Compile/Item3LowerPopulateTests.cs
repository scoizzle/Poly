using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// Item 3: policies, OnEntry/OnExit batches, and subscription bodies are
/// populated at session.Lower / GetOrLower — Domain-bound hot path binds those
/// trees (cache-identity oracles, fail-closed on miss).
/// </summary>
public class Item3LowerPopulateTests {
    [Test]
    public async Task Policy_EvaluatePolicy_UsesCachedBody_NotAgeExpression() {
        var (domain, analysis, session) = Evolve("""
            domain Guards
            Gate: entity {
              Age: Number required
              IsAdult: policy { Age >= 18 }
            }
            """);
        session.Lower(domain, analysis);
        await Assert.That(RuntimeAnalysisCache.TryGetPolicyBody(domain, "Gate", "IsAdult", out var before))
            .IsTrue();
        await Assert.That(before).IsNotNull();

        // Sentinel: constant true — Age expression would yield false for Age=16.
        RuntimeAnalysisCache.ReplacePolicyBody(domain, "Gate", "IsAdult", new Constant(true));

        var gateE = domain.Types.OfType<Entity>().First(e => e.Name == "Gate");
        var minor = DomainEntityInstance.Create(gateE,
            new Dictionary<string, object?> { ["Age"] = 16L }, domain);
        var policy = gateE.Policies.First(p => p.Name == "IsAdult");
        await Assert.That(minor.EvaluatePolicy(policy)).IsTrue();

        RuntimeAnalysisCache.ReplacePolicyBody(domain, "Gate", "IsAdult", new Constant(false));
        var adult = DomainEntityInstance.Create(gateE,
            new Dictionary<string, object?> { ["Age"] = 21L }, domain);
        await Assert.That(adult.EvaluatePolicy(policy)).IsFalse();
    }

    [Test]
    public async Task OnExit_TransitionStage_UsesCachedBody_NotReloweredAssign() {
        // Action StageTransition inlines exit into the module body; EntryExitBodies
        // are consumed by TransitionStage / ApplyInitialStageEntryEffects.
        var (domain, analysis, session) = Evolve("""
            domain Flow
            Ticket: entity {
              Note: Text
              Draft: stage {
                exit { assign Note to "left-draft" }
              }
              Done: stage { }
            }
            """);
        session.Lower(domain, analysis);
        await Assert.That(RuntimeAnalysisCache.TryGetEntryExitBody(
            domain, "Ticket", "Draft", "exit", out var exitBody)).IsTrue();
        await Assert.That(exitBody).IsNotNull();

        RuntimeAnalysisCache.ReplaceEntryExitBody(
            domain, "Ticket", "Draft", "exit",
            AssignEntityProp("Ticket", "Note", "sentinel-exit"));

        var ticketE = domain.Types.OfType<Entity>().First(e => e.Name == "Ticket");
        var ticket = DomainEntityInstance.Create(ticketE,
            new Dictionary<string, object?> { ["Note"] = "" }, domain);
        await Assert.That(ticket.CurrentStage).IsEqualTo("Draft");
        ticket.TransitionStage("Done");
        await Assert.That(ticket.CurrentStage).IsEqualTo("Done");
        await Assert.That(ticket.GetProperty<string>("Note")).IsEqualTo("sentinel-exit");
    }

    [Test]
    public async Task OnEntry_ApplyInitial_AndTransition_UsesCachedBody() {
        var (domain, analysis, session) = Evolve("""
            domain Flow
            Ticket: entity {
              Note: Text
              Draft: stage {
                entry { assign Note to "entered" }
              }
              Done: stage {
                entry { assign Note to "entered-done" }
              }
            }
            """);
        session.Lower(domain, analysis);
        await Assert.That(RuntimeAnalysisCache.TryGetEntryExitBody(
            domain, "Ticket", "Draft", "entry", out _)).IsTrue();
        await Assert.That(RuntimeAnalysisCache.TryGetEntryExitBody(
            domain, "Ticket", "Done", "entry", out _)).IsTrue();

        RuntimeAnalysisCache.ReplaceEntryExitBody(
            domain, "Ticket", "Draft", "entry",
            AssignEntityProp("Ticket", "Note", "sentinel-entry"));
        RuntimeAnalysisCache.ReplaceEntryExitBody(
            domain, "Ticket", "Done", "entry",
            AssignEntityProp("Ticket", "Note", "sentinel-done-entry"));

        var ticketE = domain.Types.OfType<Entity>().First(e => e.Name == "Ticket");
        // Create applies first-stage OnEntry via ApplyInitialStageEntryEffects → EntryExitBodies.
        var ticket = DomainEntityInstance.Create(ticketE,
            new Dictionary<string, object?> { ["Note"] = "" }, domain);
        await Assert.That(ticket.GetProperty<string>("Note")).IsEqualTo("sentinel-entry");

        // TransitionStage binds Done OnEntry from EntryExitBodies (not action-inline).
        ticket.TransitionStage("Done");
        await Assert.That(ticket.GetProperty<string>("Note")).IsEqualTo("sentinel-done-entry");
    }

    [Test]
    public async Task Subscription_FiresFromCachedBody_OnPeerTransition() {
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

        var pending = domain.Types.OfType<Entity>().First(e => e.Name == "Tracker")
            .Stages.First(s => s.Name == "Pending");
        var plan = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(pending)
            ?? throw new InvalidOperationException("missing subscription plan");
        var entry = plan.ByRelationshipName.Values.SelectMany(e => e).First();

        RuntimeAnalysisCache.ReplaceSubscriptionBody(
            domain, entry,
            AssignEntityProp("Tracker", "Status", "sentinel-sub"));

        var orderE = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var trackerE = domain.Types.OfType<Entity>().First(e => e.Name == "Tracker");
        var store = new DomainInstanceStore();
        var order = DomainEntityInstance.Create(orderE, domain: domain);
        var tracker = DomainEntityInstance.Create(trackerE,
            new Dictionary<string, object?> { ["Status"] = "Idle" }, domain);
        store.Add(order);
        store.Add(tracker);
        store.Link("Tracks", tracker, order);

        var activated = order.InvokeAction("Activate");
        await Assert.That(activated.Succeeded).IsTrue();
        await Assert.That(tracker.GetProperty<string>("Status")).IsEqualTo("sentinel-sub");
    }

    [Test]
    public async Task DomainBound_MissingPolicyBody_Throws_DoesNotRelower() {
        var (domain, analysis, session) = Evolve("""
            domain Guards
            Gate: entity {
              Age: Number required
              IsAdult: policy { Age >= 18 }
            }
            """);
        session.Lower(domain, analysis);
        RuntimeAnalysisCache.ClearPolicyBody(domain, "Gate", "IsAdult");

        var gateE = domain.Types.OfType<Entity>().First(e => e.Name == "Gate");
        var adult = DomainEntityInstance.Create(gateE,
            new Dictionary<string, object?> { ["Age"] = 21L }, domain);
        var policy = gateE.Policies.First(p => p.Name == "IsAdult");
        await Assert.That(() => adult.EvaluatePolicy(policy))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Policy body 'IsAdult' is missing");
    }

    [Test]
    public async Task DomainBound_ClearedSubscriptionBody_Throws_DoesNotRelower() {
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

        var pending = domain.Types.OfType<Entity>().First(e => e.Name == "Tracker")
            .Stages.First(s => s.Name == "Pending");
        var plan = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(pending)!;
        var entry = plan.ByRelationshipName.Values.SelectMany(e => e).First();
        RuntimeAnalysisCache.ClearSubscriptionBody(domain, entry);

        var orderE = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var trackerE = domain.Types.OfType<Entity>().First(e => e.Name == "Tracker");
        var store = new DomainInstanceStore();
        var order = DomainEntityInstance.Create(orderE, domain: domain);
        var tracker = DomainEntityInstance.Create(trackerE,
            new Dictionary<string, object?> { ["Status"] = "Idle" }, domain);
        store.Add(order);
        store.Add(tracker);
        store.Link("Tracks", tracker, order);

        await Assert.That(() => order.InvokeAction("Activate"))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Subscription body is missing");
        await Assert.That(tracker.GetProperty<string>("Status")).IsEqualTo("Idle");
    }

    [Test]
    public async Task GetOrLower_Twice_ReturnsSameModuleReference() {
        var (domain, analysis, session) = Evolve("""
            domain Guards
            Gate: entity {
              Age: Number required
              IsAdult: policy { Age >= 18 }
            }
            """);
        var first = session.Lower(domain, analysis);
        var second = RuntimeAnalysisCache.GetOrLower(
            domain, RuntimeAnalysisCache.Session(domain), analysis);
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    [Test]
    public async Task Subscription_PeerBinding_MaterializesInsideTryCatchFinally() {
        // Stage transition in subscription effects emits TryCatchFinally; peer
        // Parameter reads in the same body must still Materialize (F3 walk).
        var (domain, analysis, session) = Evolve("""
            domain Watch
            Order: entity {
              Plate: Text
              Open: stage { }
              Active: stage { }
              Activate: action { transition to Active }
            }
            Tracker: entity {
              Status: Text
              Tracks: Order
              Pending: stage {
                when Tracks Active as order {
                  assign Status to order Plate
                  transition to Done
                }
              }
              Done: stage { }
            }
            """);
        session.Lower(domain, analysis);

        var orderE = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        var trackerE = domain.Types.OfType<Entity>().First(e => e.Name == "Tracker");
        var store = new DomainInstanceStore();
        var order = DomainEntityInstance.Create(orderE,
            new Dictionary<string, object?> { ["Plate"] = "XYZ-9" }, domain);
        var tracker = DomainEntityInstance.Create(trackerE,
            new Dictionary<string, object?> { ["Status"] = "Idle" }, domain);
        store.Add(order);
        store.Add(tracker);
        store.Link("Tracks", tracker, order);

        var activated = order.InvokeAction("Activate");
        await Assert.That(activated.Succeeded).IsTrue();
        await Assert.That(tracker.GetProperty<string>("Status")).IsEqualTo("XYZ-9");
        await Assert.That(tracker.CurrentStage).IsEqualTo("Done");
    }

    private static Node AssignEntityProp(string entityName, string prop, string value) =>
        new Block([
            new Assignment(
                new Member(new Parameter("entity", new TypeReference(entityName)), prop),
                new Constant(value))
        ]);

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
