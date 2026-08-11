using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Parsing;

namespace Poly.Tests.DomainModeling;

/// <summary>
/// Composition and interaction goldens for surface extensions and graph honesty — create-in+peer,
/// multi-subscriber fan-out, unlink, multi-stage when, stage+entity peer last-writer,
/// require/if with store policies. Prefer DSL→evolve→runtime paths over pure IR.
/// </summary>
public class SurfaceExtensionCompositionTests {
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

    // ── create in → transition → entity-level peer ─────────────

    [Test]
    public async Task CreateIn_ThenActivate_EntityLevelPeer_CopiesCode() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain CreateInPeer
            Customer: entity {
              LastOrderCode: Text
              Ready: stage {
                PlaceOrder: action {
                  create in orders { Code: "FROM-CREATE" }
                }
              }
              when orders Active as order {
                assign LastOrderCode to order Code
              }
              orders: many Order
            }
            Order: entity {
              Code: Text
              Draft: stage {
                Activate: action { transition to Active }
              }
              Active: stage {}
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();

        var store = new DomainInstanceStore();
        var customer = domain.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        var cust = DomainEntityInstance.Create(customer,
            new Dictionary<string, object?> { ["LastOrderCode"] = "NONE" }, domain: domain);
        store.Add(cust);

        var place = cust.InvokeAction("PlaceOrder");
        await Assert.That(place.Succeeded).IsTrue();
        await Assert.That(cust.CreatedChildren.Count).IsEqualTo(1);
        var order = cust.CreatedChildren[0];
        await Assert.That(store.IsLinked("orders", cust, order)).IsTrue();
        await Assert.That(order.GetProperty<string>("Code")).IsEqualTo("FROM-CREATE");

        await Assert.That(order.InvokeAction("Activate").Succeeded).IsTrue();
        await Assert.That(cust.GetProperty<string>("LastOrderCode")).IsEqualTo("FROM-CREATE");
    }

    // ── unlink honesty ─────────────────────────────────────────

    [Test]
    public async Task Unlink_StopsPeerWhen_AndExistsBecomesFalse() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain UnlinkPeer
            Tracker: entity {
              Status: Text
              Tracks: Order
              HasOrder: policy { Tracks exists }
              Idle: stage {}
              when Tracks Active as order {
                assign Status to order Code
              }
            }
            Order: entity {
              Code: Text
              Draft: stage {
                Activate: action { transition to Active }
                Reset: action { transition to Draft }
              }
              Active: stage {
                Reset: action { transition to Draft }
              }
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();

        var store = new DomainInstanceStore();
        var trackerEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Tracker");
        var orderEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var tracker = DomainEntityInstance.Create(trackerEntity,
            new Dictionary<string, object?> { ["Status"] = "UNSET" }, domain: domain);
        var order = DomainEntityInstance.Create(orderEntity,
            new Dictionary<string, object?> { ["Code"] = "ORD-1" }, domain: domain);
        store.Add(tracker);
        store.Add(order);
        store.Link("Tracks", tracker, order);

        var hasOrder = trackerEntity.Policies.Single(p => p.Name == "HasOrder");
        await Assert.That(tracker.EvaluatePolicy(hasOrder)).IsTrue();

        await Assert.That(order.InvokeAction("Activate").Succeeded).IsTrue();
        await Assert.That(tracker.GetProperty<string>("Status")).IsEqualTo("ORD-1");

        store.Unlink("Tracks", tracker, order);
        await Assert.That(tracker.EvaluatePolicy(hasOrder)).IsFalse();

        await Assert.That(order.InvokeAction("Reset").Succeeded).IsTrue();
        tracker.SetProperty("Status", "AFTER-UNLINK");
        await Assert.That(order.InvokeAction("Activate").Succeeded).IsTrue();
        // Unlinked: peer when must not fire
        await Assert.That(tracker.GetProperty<string>("Status")).IsEqualTo("AFTER-UNLINK");
    }

    // ── multi-subscriber fan-out ───────────────────────────────

    [Test]
    public async Task TwoSubscribers_OneOrderActive_BothEntityLevelPeersFire() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain TwoSubs
            Tracker: entity {
              LastCode: Text
              Tracks: Order
              Idle: stage {}
              when Tracks Active as order {
                assign LastCode to order Code
              }
            }
            Order: entity {
              Code: Text
              Draft: stage {
                Activate: action { transition to Active }
              }
              Active: stage {}
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();

        var store = new DomainInstanceStore();
        var trackerEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Tracker");
        var orderEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var t1 = DomainEntityInstance.Create(trackerEntity,
            new Dictionary<string, object?> { ["LastCode"] = "NONE" }, domain: domain);
        var t2 = DomainEntityInstance.Create(trackerEntity,
            new Dictionary<string, object?> { ["LastCode"] = "NONE" }, domain: domain);
        var order = DomainEntityInstance.Create(orderEntity,
            new Dictionary<string, object?> { ["Code"] = "SHARED-99" }, domain: domain);
        store.Add(t1);
        store.Add(t2);
        store.Add(order);
        store.Link("Tracks", t1, order);
        store.Link("Tracks", t2, order);

        await Assert.That(order.InvokeAction("Activate").Succeeded).IsTrue();
        await Assert.That(t1.GetProperty<string>("LastCode")).IsEqualTo("SHARED-99");
        await Assert.That(t2.GetProperty<string>("LastCode")).IsEqualTo("SHARED-99");
    }

    // ── multi-stage when list ──────────────────────────────────

    [Test]
    public async Task MultiStageWhen_Peer_FiresOnSecondStageAsWell() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain MultiStageWhen
            Tracker: entity {
              Status: Text
              Tracks: Order
              Idle: stage {}
              when Tracks Active, Completed as order {
                assign Status to order Code
              }
            }
            Order: entity {
              Code: Text
              Draft: stage {
                Activate: action { transition to Active }
              }
              Active: stage {
                Complete: action { transition to Completed }
              }
              Completed: stage {}
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();

        var store = new DomainInstanceStore();
        var trackerEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Tracker");
        var orderEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var tracker = DomainEntityInstance.Create(trackerEntity,
            new Dictionary<string, object?> { ["Status"] = "UNSET" }, domain: domain);
        var order = DomainEntityInstance.Create(orderEntity,
            new Dictionary<string, object?> { ["Code"] = "MS-1" }, domain: domain);
        store.Add(tracker);
        store.Add(order);
        store.Link("Tracks", tracker, order);

        await Assert.That(order.InvokeAction("Activate").Succeeded).IsTrue();
        await Assert.That(tracker.GetProperty<string>("Status")).IsEqualTo("MS-1");

        tracker.SetProperty("Status", "CLEARED");
        order.SetProperty("Code", "MS-2");
        await Assert.That(order.InvokeAction("Complete").Succeeded).IsTrue();
        await Assert.That(tracker.GetProperty<string>("Status")).IsEqualTo("MS-2");
    }

    // ── stage peer + entity peer same notify ───────────────────

    [Test]
    public async Task StageAndEntityPeer_SameNotify_StageFirstThenEntity_LastWriterOnSharedProp() {
        // Stage assigns StageNote from peer; entity assigns LastCode from peer.
        // Shared Status: stage writes STAGE, entity writes ENTITY → last writer = ENTITY.
        var (domain, analysis) = ParseAndAnalyze("""
            domain StageEntityPeer
            Tracker: entity {
              Status: Text
              StageNote: Text
              LastCode: Text
              Tracks: Order
              Watching: stage {
                when Tracks Active as order {
                  assign StageNote to order Code
                  assign Status to "STAGE"
                }
              }
              when Tracks Active as order {
                assign LastCode to order Code
                assign Status to "ENTITY"
              }
            }
            Order: entity {
              Code: Text
              Draft: stage {
                Activate: action { transition to Active }
              }
              Active: stage {}
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();

        var store = new DomainInstanceStore();
        var trackerEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Tracker");
        var orderEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var tracker = DomainEntityInstance.Create(trackerEntity,
            new Dictionary<string, object?> {
                ["Status"] = "UNSET",
                ["StageNote"] = "NONE",
                ["LastCode"] = "NONE"
            }, domain: domain);
        var order = DomainEntityInstance.Create(orderEntity,
            new Dictionary<string, object?> { ["Code"] = "BOTH-7" }, domain: domain);
        store.Add(tracker);
        store.Add(order);
        store.Link("Tracks", tracker, order);

        await Assert.That(tracker.CurrentStage).IsEqualTo("Watching");
        await Assert.That(order.InvokeAction("Activate").Succeeded).IsTrue();

        await Assert.That(tracker.GetProperty<string>("StageNote")).IsEqualTo("BOTH-7");
        await Assert.That(tracker.GetProperty<string>("LastCode")).IsEqualTo("BOTH-7");
        await Assert.That(tracker.GetProperty<string>("Status")).IsEqualTo("ENTITY");
    }

    // ── multi-link path-prefix (also unit-tested elsewhere; DSL path) ──

    [Test]
    public async Task PathPrefix_OnMany_RejectedAtAnalysis_DslDomain() {
        // Product: bare path-prefix on many is analysis fail-closed (use any/all).
        // Multi-link runtime throw is covered for IR OneToMany policies elsewhere.
        var poly = """
            domain MultiLink
            Customer: entity {
              Name: Text
              orders: many Order
              BadBare: policy { orders Code is "X" }
            }
            Order: entity {
              Code: Text
            }
            """;
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        await Assert.That(result.Succeeded).IsFalse();
        var messages = string.Join(" ", result.Analysis.Diagnostics.Select(d => d.Message));
        await Assert.That(messages).Contains("many");
        await Assert.That(messages.Contains("quantifier", StringComparison.OrdinalIgnoreCase)
            || messages.Contains("path-prefix", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    // ── require + exists (store-linked) ────────────────────────

    [Test]
    public async Task Require_HasOrdersExists_BlocksWhenUnlinked_PassesWhenLinked() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain RequireExists
            Customer: entity {
              Status: Text
              orders: many Order
              HasOrders: policy { orders exists }
              Draft: stage {
                Ship: action
                  require HasOrders
                {
                  assign Status to "shipped"
                }
              }
            }
            Order: entity {
              Code: Text
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();

        var store = new DomainInstanceStore();
        var customer = domain.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        var orderEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var cust = DomainEntityInstance.Create(customer,
            new Dictionary<string, object?> { ["Status"] = "open" }, domain: domain);
        var order = DomainEntityInstance.Create(orderEntity,
            new Dictionary<string, object?> { ["Code"] = "1" }, domain: domain);
        store.Add(cust);
        store.Add(order);

        var blocked = cust.InvokeAction("Ship");
        await Assert.That(blocked.Succeeded).IsFalse();
        await Assert.That(cust.GetProperty<string>("Status")).IsEqualTo("open");

        store.Link("orders", cust, order);
        var ok = cust.InvokeAction("Ship");
        await Assert.That(ok.Succeeded).IsTrue();
        await Assert.That(cust.GetProperty<string>("Status")).IsEqualTo("shipped");
    }

    // ── if (Rel exists) in action ──────────────────────────────

    [Test]
    public async Task If_RelExists_BranchesOnStoreLinks() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain IfExists
            Customer: entity {
              Status: Text
              orders: many Order
              Mark: action {
                if (orders exists) {
                  assign Status to "has"
                } else {
                  assign Status to "none"
                }
              }
            }
            Order: entity {
              Code: Text
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();

        var store = new DomainInstanceStore();
        var customer = domain.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        var orderEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var cust = DomainEntityInstance.Create(customer,
            new Dictionary<string, object?> { ["Status"] = "?" }, domain: domain);
        var order = DomainEntityInstance.Create(orderEntity,
            new Dictionary<string, object?> { ["Code"] = "1" }, domain: domain);
        store.Add(cust);
        store.Add(order);

        await Assert.That(cust.InvokeAction("Mark").Succeeded).IsTrue();
        await Assert.That(cust.GetProperty<string>("Status")).IsEqualTo("none");

        store.Link("orders", cust, order);
        await Assert.That(cust.InvokeAction("Mark").Succeeded).IsTrue();
        await Assert.That(cust.GetProperty<string>("Status")).IsEqualTo("has");
    }

    // ── analysis: unused binder OK; wrong root fails ───────────

    [Test]
    public async Task PeerBinder_Unused_AnalysisAccepts() {
        var (_, analysis) = ParseAndAnalyze("""
            domain UnusedBinder
            Tracker: entity {
              Status: Text
              Tracks: Order
              Pending: stage {
                when Tracks Active as order {
                  assign Status to "ping"
                }
              }
            }
            Order: entity {
              Code: Text
              Draft: stage {}
              Active: stage {}
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionEffectBinding
            && d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    [Test]
    public async Task PeerBinder_BodyUsesDifferentRoot_AnalysisError() {
        var poly = """
            domain WrongBinder
            Tracker: entity {
              Status: Text
              Tracks: Order
              Pending: stage {
                when Tracks Active as order {
                  assign Status to ord Code
                }
              }
            }
            Order: entity {
              Code: Text
              Draft: stage {}
              Active: stage {}
            }
            """;
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        await Assert.That(result.Succeeded).IsFalse();
        var messages = string.Join(" ", result.Analysis.Diagnostics.Select(d => d.Message));
        await Assert.That(
            messages.Contains("ord", StringComparison.Ordinal)
            || messages.Contains("binder", StringComparison.OrdinalIgnoreCase)
            || messages.Contains("peer", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    // ── Quantifier empty-set honesty (runtime) ─────────────────────────

    [Test]
    public async Task Quantifiers_EmptyLinks_NoVacuousAll_NoneTrue_CountZero() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain EmptyQ3
            Customer: entity {
              Name: Text
              orders: many Order
              AnyBig: policy { any orders where Total > 100 }
              AllBig: policy { all orders where Total > 100 }
              NoneBig: policy { none orders where Total > 100 }
              OrderCount: policy { count orders is 0 }
            }
            Order: entity {
              Total: Number
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();

        var store = new DomainInstanceStore();
        var customer = domain.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        var cust = DomainEntityInstance.Create(customer,
            new Dictionary<string, object?> { ["Name"] = "A" }, domain: domain);
        store.Add(cust);

        await Assert.That(cust.EvaluatePolicy(customer.Policies.Single(p => p.Name == "AnyBig")))
            .IsFalse();
        await Assert.That(cust.EvaluatePolicy(customer.Policies.Single(p => p.Name == "AllBig")))
            .IsFalse(); // no vacuous true
        await Assert.That(cust.EvaluatePolicy(customer.Policies.Single(p => p.Name == "NoneBig")))
            .IsTrue();
        await Assert.That(cust.EvaluatePolicy(customer.Policies.Single(p => p.Name == "OrderCount")))
            .IsTrue();
    }
}