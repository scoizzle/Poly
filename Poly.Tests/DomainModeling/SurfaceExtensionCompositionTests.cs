using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling;

/// <summary>
/// Composition and interaction goldens for surface extensions and graph honesty — create-in+peer,
/// multi-subscriber fan-out, unlink, multi-stage when, stage+entity peer last-writer,
/// require/if with store policies. Prefer DSL→evolve→runtime paths over pure IR.
/// </summary>
public class SurfaceExtensionCompositionTests {
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
              customer: Customer via orders
              Draft: stage {
                Activate: action { transition to Active }
              }
              Active: stage {}
            }
            """);
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();

        var customer = domain.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        var cust = exec.Create((customer).Name, new Dictionary<string, object?> { ["LastOrderCode"] = "NONE" }).Instance!;
        var place = exec.Invoke(cust, "PlaceOrder");
        await Assert.That(place.Succeeded).IsTrue();
        await Assert.That(place.CreatedChildren.Count).IsEqualTo(1);
        var order = place.CreatedChildren[0];
        await Assert.That(exec.IsLinked("orders", cust, order)).IsTrue();
        await Assert.That((string)order.Bag["Code"]!).IsEqualTo("FROM-CREATE");

        await Assert.That(exec.Invoke(order, "Activate").Succeeded).IsTrue();
        await Assert.That((string)cust.Bag["LastOrderCode"]!).IsEqualTo("FROM-CREATE");
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
              trackers: many Tracker via Tracks
              Draft: stage {
                Activate: action { transition to Active }
                Reset: action { transition to Draft }
              }
              Active: stage {
                Reset: action { transition to Draft }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();

        var trackerEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Tracker");
        var orderEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var tracker = exec.Create((trackerEntity).Name, new Dictionary<string, object?> { ["Status"] = "UNSET" }).Instance!;
        var order = exec.Create((orderEntity).Name, new Dictionary<string, object?> { ["Code"] = "ORD-1" }).Instance!;
        exec.Link("Tracks", tracker, order);

        var hasOrder = trackerEntity.Policies.Single(p => p.Name == "HasOrder");
        await Assert.That(exec.EvaluatePolicy(tracker, (hasOrder).Name)).IsTrue();

        await Assert.That(exec.Invoke(order, "Activate").Succeeded).IsTrue();
        await Assert.That((string)tracker.Bag["Status"]!).IsEqualTo("ORD-1");

        exec.Unlink("Tracks", tracker, order);
        await Assert.That(exec.EvaluatePolicy(tracker, (hasOrder).Name)).IsFalse();

        await Assert.That(exec.Invoke(order, "Reset").Succeeded).IsTrue();
        tracker.Bag["Status"] = "AFTER-UNLINK";
        await Assert.That(exec.Invoke(order, "Activate").Succeeded).IsTrue();
        // Unlinked: peer when must not fire
        await Assert.That((string)tracker.Bag["Status"]!).IsEqualTo("AFTER-UNLINK");
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
              trackers: many Tracker via Tracks
              Draft: stage {
                Activate: action { transition to Active }
              }
              Active: stage {}
            }
            """);
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();

        var trackerEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Tracker");
        var orderEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var t1 = exec.Create((trackerEntity).Name, new Dictionary<string, object?> { ["LastCode"] = "NONE" }).Instance!;
        var t2 = exec.Create((trackerEntity).Name, new Dictionary<string, object?> { ["LastCode"] = "NONE" }).Instance!;
        var order = exec.Create((orderEntity).Name, new Dictionary<string, object?> { ["Code"] = "SHARED-99" }).Instance!;
        exec.Link("Tracks", t1, order);
        exec.Link("Tracks", t2, order);

        await Assert.That(exec.Invoke(order, "Activate").Succeeded).IsTrue();
        await Assert.That((string)t1.Bag["LastCode"]!).IsEqualTo("SHARED-99");
        await Assert.That((string)t2.Bag["LastCode"]!).IsEqualTo("SHARED-99");
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
              trackers: many Tracker via Tracks
              Draft: stage {
                Activate: action { transition to Active }
              }
              Active: stage {
                Complete: action { transition to Completed }
              }
              Completed: stage {}
            }
            """);
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();

        var trackerEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Tracker");
        var orderEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var tracker = exec.Create((trackerEntity).Name, new Dictionary<string, object?> { ["Status"] = "UNSET" }).Instance!;
        var order = exec.Create((orderEntity).Name, new Dictionary<string, object?> { ["Code"] = "MS-1" }).Instance!;
        exec.Link("Tracks", tracker, order);

        await Assert.That(exec.Invoke(order, "Activate").Succeeded).IsTrue();
        await Assert.That((string)tracker.Bag["Status"]!).IsEqualTo("MS-1");

        tracker.Bag["Status"] = "CLEARED";
        order.Bag["Code"] = "MS-2";
        await Assert.That(exec.Invoke(order, "Complete").Succeeded).IsTrue();
        await Assert.That((string)tracker.Bag["Status"]!).IsEqualTo("MS-2");
    }

    // ── stage peer + entity peer same notify ───────────────────

    [Test]
    public async Task StageAndEntityPeer_SameNotify_CompiledNotifyBodyOrder_LastWriterOnSharedProp() {
        // Stage assigns StageNote from peer; entity assigns LastCode from peer.
        // Compiled Notify body calls entity-level When… then stage-scoped When…
        // Shared Status: last writer = STAGE.
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
              trackers: many Tracker via Tracks
              Draft: stage {
                Activate: action { transition to Active }
              }
              Active: stage {}
            }
            """);
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();

        var trackerEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Tracker");
        var orderEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var tracker = exec.Create((trackerEntity).Name, new Dictionary<string, object?> {
            ["Status"] = "UNSET",
            ["StageNote"] = "NONE",
            ["LastCode"] = "NONE"
        }).Instance!;
        var order = exec.Create((orderEntity).Name, new Dictionary<string, object?> { ["Code"] = "BOTH-7" }).Instance!;
        exec.Link("Tracks", tracker, order);

        await Assert.That(ModuleReads.Stage(tracker)).IsEqualTo("Watching");
        await Assert.That(exec.Invoke(order, "Activate").Succeeded).IsTrue();

        await Assert.That((string)tracker.Bag["StageNote"]!).IsEqualTo("BOTH-7");
        await Assert.That((string)tracker.Bag["LastCode"]!).IsEqualTo("BOTH-7");
        await Assert.That((string)tracker.Bag["Status"]!).IsEqualTo("STAGE");
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
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
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
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();

        var customer = domain.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        var orderEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var cust = exec.Create((customer).Name, new Dictionary<string, object?> { ["Status"] = "open" }).Instance!;
        var order = exec.Create((orderEntity).Name, new Dictionary<string, object?> { ["Code"] = "1" }).Instance!;
        var blocked = exec.Invoke(cust, "Ship");
        await Assert.That(blocked.Succeeded).IsFalse();
        await Assert.That((string)cust.Bag["Status"]!).IsEqualTo("open");

        exec.Link("orders", cust, order);
        var ok = exec.Invoke(cust, "Ship");
        await Assert.That(ok.Succeeded).IsTrue();
        await Assert.That((string)cust.Bag["Status"]!).IsEqualTo("shipped");
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
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();

        var customer = domain.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        var orderEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var cust = exec.Create((customer).Name, new Dictionary<string, object?> { ["Status"] = "?" }).Instance!;
        var order = exec.Create((orderEntity).Name, new Dictionary<string, object?> { ["Code"] = "1" }).Instance!;
        await Assert.That(exec.Invoke(cust, "Mark").Succeeded).IsTrue();
        await Assert.That((string)cust.Bag["Status"]!).IsEqualTo("none");

        exec.Link("orders", cust, order);
        await Assert.That(exec.Invoke(cust, "Mark").Succeeded).IsTrue();
        await Assert.That((string)cust.Bag["Status"]!).IsEqualTo("has");
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
              trackers: many Tracker via Tracks
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
              trackers: many Tracker via Tracks
              Draft: stage {}
              Active: stage {}
            }
            """;
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
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
        var exec = ModuleExec.For(domain);
        await Assert.That(analysis.HasErrors).IsFalse();

        var customer = domain.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        var cust = exec.Create((customer).Name, new Dictionary<string, object?> { ["Name"] = "A" }).Instance!;
        await Assert.That(exec.EvaluatePolicy(cust, (customer.Policies.Single(p => p.Name == "AnyBig")).Name))
            .IsFalse();
        await Assert.That(exec.EvaluatePolicy(cust, (customer.Policies.Single(p => p.Name == "AllBig")).Name))
            .IsFalse(); // no vacuous true
        await Assert.That(exec.EvaluatePolicy(cust, (customer.Policies.Single(p => p.Name == "NoneBig")).Name))
            .IsTrue();
        await Assert.That(exec.EvaluatePolicy(cust, (customer.Policies.Single(p => p.Name == "OrderCount")).Name))
            .IsTrue();
    }
}