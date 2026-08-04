using System.Text.Json;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// Surface-extension dogfood — agent-facing MCP path for shipped surface extensions:
/// export peer, entity-level when and peer binder, owned path-prefix,
/// store-aware Rel exists. Prefer these over unit goldens when proving product honesty.
/// </summary>
public class SurfaceExtensionDogfoodTests {
    /// <summary>
    /// Shared surface-extension domain: entity-level peer when, stage-scoped notification when,
    /// owned profile policies, Rel exists. Used by multiple goldens.
    /// </summary>
    private const string SharedSurfaceExtensionDomain = """
        domain SurfaceExtensionDogfood

        Customer: entity {
          Name: Text
          Status: Text
          LastOrderCode: Text
          profile: owned Profile
          orders: many Order
          IsUrban: policy { profile City is "Metropolis" }
          HasProfile: policy { profile exists }
          HasOrders: policy { orders exists }

          Idle: stage {}
          Pending: stage {
            when orders Active {
              assign Status to "Notified"
            }
          }

          when orders Active as order {
            assign LastOrderCode to order Code
          }
        }

        Profile: entity {
          City: Text
        }

        Order: entity {
          Code: Text
          Draft: stage {
            Activate: action {
              transition to Active
            }
          }
          Active: stage {}
        }
        """;

    // ── Apply / analysis ───────────────────────────────────────

    [Test]
    public async Task ApplyDsl_SurfaceExtensions_AnalyzesClean() {
        var (sessionId, _) = SessionStore.Create("SurfaceExtensionDogfood");
        var apply = DslTool.ApplyDsl(sessionId, SharedSurfaceExtensionDomain);
        await Assert.That(apply.Success).IsTrue();

        SessionStore.TryGet(sessionId, out var state);
        await Assert.That(state).IsNotNull();
        var analysis = DomainModelAnalyzer.Analyze(state!.Domain);
        await Assert.That(analysis.HasStructuralFailure).IsFalse();
        await Assert.That(analysis.HasErrors).IsFalse();

        var customer = state.Domain.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        await Assert.That(customer.Subscriptions.Count).IsEqualTo(1);
        await Assert.That(customer.Subscriptions[0].PeerBinding).IsEqualTo("order");
        await Assert.That(customer.Stages.Single(s => s.Name == "Pending").Subscriptions.Count)
            .IsEqualTo(1);
        await Assert.That(customer.Policies.Select(p => p.Name).ToArray())
            .IsEquivalentTo(["IsUrban", "HasProfile", "HasOrders"]);
    }

    [Test]
    public async Task ExportDsl_SurfaceExtensions_RoundTripsPeerEntityWhenAndOwned() {
        var (sessionId, _) = SessionStore.Create("SurfaceExtensionDogfood");
        await Assert.That(DslTool.ApplyDsl(sessionId, SharedSurfaceExtensionDomain).Success).IsTrue();

        var export = DslTool.ExportDsl(sessionId);
        await Assert.That(export.Success).IsTrue();
        var poly = ExtractPoly(export);
        await Assert.That(poly).Contains("when orders Active as order");
        await Assert.That(poly).Contains("profile: owned Profile");
        await Assert.That(poly).Contains("profile exists");
        await Assert.That(poly).Contains("orders exists");
        await Assert.That(poly).Contains("profile City is \"Metropolis\"");

        var reapply = DslTool.ApplyDsl(sessionId, poly);
        await Assert.That(reapply.Success).IsTrue();
        SessionStore.TryGet(sessionId, out var state);
        var customer = state!.Domain.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        await Assert.That(customer.Subscriptions[0].PeerBinding).IsEqualTo("order");
    }

    // ── L: entity-level when + peer binder (MCP invoke path) ───

    [Test]
    public async Task EntityLevel_PeerWhen_CopiesPeerCode_RegardlessOfSubscriberStage() {
        // Entity-level: entity-level when fires in Idle (not Pending); peer binder copies order Code.
        var (sessionId, _) = SessionStore.Create("SurfaceExtensionDogfood");
        await Assert.That(DslTool.ApplyDsl(sessionId, SharedSurfaceExtensionDomain).Success).IsTrue();

        var custId = CreateAndId(sessionId, "Customer",
            """{"Name":"Alice","Status":"Quiet","LastOrderCode":"NONE"}""");
        var orderId = CreateAndId(sessionId, "Order", """{"Code":"ORD-42"}""");

        // Customer starts in Idle (first stage) — stage-scoped Pending when must not fire.
        await Assert.That(GetProp(sessionId, custId, "Status")).IsEqualTo("Quiet");
        await Assert.That(GetStage(sessionId, custId)).IsEqualTo("Idle");

        var link = RuntimeTool.LinkInstances(sessionId, custId, "orders", orderId);
        await Assert.That(link.Success).IsTrue();

        var activate = RuntimeTool.InvokeAction(sessionId, orderId, "Activate");
        await Assert.That(activate.Success).IsTrue();
        await Assert.That(activate.Message).Contains("Active");

        await Assert.That(GetProp(sessionId, custId, "LastOrderCode")).IsEqualTo("ORD-42");
        // Still Quiet — stage-scoped when only active in Pending
        await Assert.That(GetProp(sessionId, custId, "Status")).IsEqualTo("Quiet");
    }

    [Test]
    public async Task StageScoped_When_FiresOnlyWhileSubscriberInPending() {
        // Contrast: move customer into Pending, then order Active → Status Notified + peer copy.
        var (sessionId, _) = SessionStore.Create("StageScopedVsEntityLevel");
        var dsl = """
            domain StageScopedVsEntityLevel
            Customer: entity {
              Status: Text
              LastOrderCode: Text
              orders: many Order
              Idle: stage {
                EnterPending: action { transition to Pending }
              }
              Pending: stage {
                when orders Active {
                  assign Status to "Notified"
                }
              }
              when orders Active as order {
                assign LastOrderCode to order Code
              }
            }
            Order: entity {
              Code: Text
              Draft: stage {
                Activate: action { transition to Active }
              }
              Active: stage {}
            }
            """;
        await Assert.That(DslTool.ApplyDsl(sessionId, dsl).Success).IsTrue();

        var custId = CreateAndId(sessionId, "Customer",
            """{"Status":"Quiet","LastOrderCode":"NONE"}""");
        var orderId = CreateAndId(sessionId, "Order", """{"Code":"ORD-7"}""");
        await Assert.That(RuntimeTool.LinkInstances(sessionId, custId, "orders", orderId).Success)
            .IsTrue();

        await Assert.That(RuntimeTool.InvokeAction(sessionId, custId, "EnterPending").Success)
            .IsTrue();
        await Assert.That(GetStage(sessionId, custId)).IsEqualTo("Pending");

        await Assert.That(RuntimeTool.InvokeAction(sessionId, orderId, "Activate").Success)
            .IsTrue();

        await Assert.That(GetProp(sessionId, custId, "Status")).IsEqualTo("Notified");
        await Assert.That(GetProp(sessionId, custId, "LastOrderCode")).IsEqualTo("ORD-7");
    }

    [Test]
    public async Task StageScoped_PeerWhen_CopiesPeerCode_ViaMcp() {
        var (sessionId, _) = SessionStore.Create("StageScopedPeerBinding");
        var dsl = """
            domain StageScopedPeerBinding
            Tracker: entity {
              Status: Text
              Tracks: Order
              Watching: stage {
                when Tracks Active as order {
                  assign Status to order Code
                }
              }
            }
            Order: entity {
              Code: Text
              Draft: stage {
                Activate: action { transition to Active }
              }
              Active: stage {}
            }
            """;
        await Assert.That(DslTool.ApplyDsl(sessionId, dsl).Success).IsTrue();

        var trackerId = CreateAndId(sessionId, "Tracker", """{"Status":"UNSET"}""");
        var orderId = CreateAndId(sessionId, "Order", """{"Code":"PEER-9"}""");
        await Assert.That(RuntimeTool.LinkInstances(sessionId, trackerId, "Tracks", orderId).Success)
            .IsTrue();
        await Assert.That(GetStage(sessionId, trackerId)).IsEqualTo("Watching");

        await Assert.That(RuntimeTool.InvokeAction(sessionId, orderId, "Activate").Success)
            .IsTrue();
        await Assert.That(GetProp(sessionId, trackerId, "Status")).IsEqualTo("PEER-9");
    }

    // ── O: owned path-prefix policies ──────────────────────────

    [Test]
    public async Task OwnedPolicy_CreateLinkEvaluate_TrueAndFalse() {
        var (sessionId, _) = SessionStore.Create("SurfaceExtensionDogfood");
        await Assert.That(DslTool.ApplyDsl(sessionId, SharedSurfaceExtensionDomain).Success).IsTrue();

        var aliceId = CreateAndId(sessionId, "Customer",
            """{"Name":"Alice","Status":"x","LastOrderCode":"n"}""");
        var bobId = CreateAndId(sessionId, "Customer",
            """{"Name":"Bob","Status":"x","LastOrderCode":"n"}""");
        var urbanId = CreateAndId(sessionId, "Profile", """{"City":"Metropolis"}""");
        var ruralId = CreateAndId(sessionId, "Profile", """{"City":"Gotham"}""");

        await Assert.That(RuntimeTool.LinkInstances(sessionId, aliceId, "profile", urbanId).Success)
            .IsTrue();
        await Assert.That(RuntimeTool.LinkInstances(sessionId, bobId, "profile", ruralId).Success)
            .IsTrue();

        var urban = PolicyTool.EvaluatePolicy(sessionId, "Customer", "IsUrban", instanceId: aliceId);
        await Assert.That(urban.Success).IsTrue();
        await Assert.That(urban.Message).Contains("true");

        var rural = PolicyTool.EvaluatePolicy(sessionId, "Customer", "IsUrban", instanceId: bobId);
        await Assert.That(rural.Success).IsTrue();
        await Assert.That(rural.Message).Contains("false");
    }

    [Test]
    public async Task OwnedPolicy_Unlinked_FailsClosed() {
        // No vacuous true when path-prefix has no outbound link.
        var (sessionId, _) = SessionStore.Create("SurfaceExtensionDogfood");
        await Assert.That(DslTool.ApplyDsl(sessionId, SharedSurfaceExtensionDomain).Success).IsTrue();

        var custId = CreateAndId(sessionId, "Customer",
            """{"Name":"Alone","Status":"x","LastOrderCode":"n"}""");

        var eval = PolicyTool.EvaluatePolicy(sessionId, "Customer", "IsUrban", instanceId: custId);
        await Assert.That(eval.Success).IsFalse();
    }

    // ── Relationship exists (store-aware) ───────────────────────

    [Test]
    public async Task RelExists_LinkedAndUnlinked_EvaluatesHonestly() {
        var (sessionId, _) = SessionStore.Create("SurfaceExtensionDogfood");
        await Assert.That(DslTool.ApplyDsl(sessionId, SharedSurfaceExtensionDomain).Success).IsTrue();

        var withProfile = CreateAndId(sessionId, "Customer",
            """{"Name":"Has","Status":"x","LastOrderCode":"n"}""");
        var without = CreateAndId(sessionId, "Customer",
            """{"Name":"No","Status":"x","LastOrderCode":"n"}""");
        var profileId = CreateAndId(sessionId, "Profile", """{"City":"X"}""");
        await Assert.That(RuntimeTool.LinkInstances(sessionId, withProfile, "profile", profileId)
            .Success).IsTrue();

        var has = PolicyTool.EvaluatePolicy(sessionId, "Customer", "HasProfile",
            instanceId: withProfile);
        await Assert.That(has.Success).IsTrue();
        await Assert.That(has.Message).Contains("true");

        var missing = PolicyTool.EvaluatePolicy(sessionId, "Customer", "HasProfile",
            instanceId: without);
        await Assert.That(missing.Success).IsTrue();
        await Assert.That(missing.Message).Contains("false");
    }

    [Test]
    public async Task RelExists_Many_OrdersLinked_True() {
        var (sessionId, _) = SessionStore.Create("SurfaceExtensionDogfood");
        await Assert.That(DslTool.ApplyDsl(sessionId, SharedSurfaceExtensionDomain).Success).IsTrue();

        var custId = CreateAndId(sessionId, "Customer",
            """{"Name":"C","Status":"x","LastOrderCode":"n"}""");
        var orderId = CreateAndId(sessionId, "Order", """{"Code":"1"}""");

        var before = PolicyTool.EvaluatePolicy(sessionId, "Customer", "HasOrders",
            instanceId: custId);
        await Assert.That(before.Success).IsTrue();
        await Assert.That(before.Message).Contains("false");

        await Assert.That(RuntimeTool.LinkInstances(sessionId, custId, "orders", orderId).Success)
            .IsTrue();

        var after = PolicyTool.EvaluatePolicy(sessionId, "Customer", "HasOrders",
            instanceId: custId);
        await Assert.That(after.Success).IsTrue();
        await Assert.That(after.Message).Contains("true");
    }

    // ── E: C# export peer handlers via MCP ─────────────────────

    [Test]
    public async Task ExportDomainToCSharp_PeerDependentWhen_EmitsPeerParamAndNotify() {
        var (sessionId, _) = SessionStore.Create("PeerExport");
        var dsl = """
            domain PeerExport
            Tracker: entity {
              Status: Text
              Tracks: Order
              Pending: stage {
                when Tracks Active as order {
                  assign Status to order Code
                }
              }
            }
            Order: entity {
              Code: Text
              Draft: stage {}
              Active: stage {}
            }
            """;
        await Assert.That(DslTool.ApplyDsl(sessionId, dsl).Success).IsTrue();

        var export = OracleTool.ExportDomainToCSharp(sessionId);
        await Assert.That(export.Success).IsTrue();
        var csharp = ExtractCSharp(export);
        await Assert.That(csharp).IsNotNull().And.IsNotEmpty();

        // Peer handler shape: typed param + binder name; notify uses this.
        await Assert.That(csharp!).Contains("WhenOrderActive");
        await Assert.That(csharp).Contains("Order order");
        await Assert.That(csharp.Contains("NotifyActiveSubscribers")
            || csharp.Contains("WhenOrderActive(")).IsTrue();
    }

    [Test]
    public async Task ExportDomainToCSharp_EntityLevelPeerWhen_EmitsHandler() {
        var (sessionId, _) = SessionStore.Create("SurfaceExtensionDogfood");
        await Assert.That(DslTool.ApplyDsl(sessionId, SharedSurfaceExtensionDomain).Success).IsTrue();

        var export = OracleTool.ExportDomainToCSharp(sessionId);
        await Assert.That(export.Success).IsTrue();
        var csharp = ExtractCSharp(export);
        await Assert.That(csharp!).Contains("WhenOrderActive");
        await Assert.That(csharp).Contains("Order order");
    }

    // ── Vertical slice: full agent path ────────────────────────

    [Test]
    public async Task FullPath_CreateLinkActivate_PeerPolicyExistsExport() {
        // One session exercises L peer + O owned + exists + export honesty.
        var (sessionId, _) = SessionStore.Create("SurfaceExtensionDogfood");
        await Assert.That(DslTool.ApplyDsl(sessionId, SharedSurfaceExtensionDomain).Success).IsTrue();

        var custId = CreateAndId(sessionId, "Customer",
            """{"Name":"Full","Status":"Quiet","LastOrderCode":"NONE"}""");
        var orderId = CreateAndId(sessionId, "Order", """{"Code":"FULL-1"}""");
        var profileId = CreateAndId(sessionId, "Profile", """{"City":"Metropolis"}""");

        await Assert.That(RuntimeTool.LinkInstances(sessionId, custId, "orders", orderId).Success)
            .IsTrue();
        await Assert.That(RuntimeTool.LinkInstances(sessionId, custId, "profile", profileId)
            .Success).IsTrue();

        // Policies before transition
        var hasOrders = PolicyTool.EvaluatePolicy(sessionId, "Customer", "HasOrders",
            instanceId: custId);
        await Assert.That(hasOrders.Message).Contains("true");
        var isUrban = PolicyTool.EvaluatePolicy(sessionId, "Customer", "IsUrban",
            instanceId: custId);
        await Assert.That(isUrban.Message).Contains("true");

        await Assert.That(RuntimeTool.InvokeAction(sessionId, orderId, "Activate").Success)
            .IsTrue();
        await Assert.That(GetProp(sessionId, custId, "LastOrderCode")).IsEqualTo("FULL-1");

        var exportCs = OracleTool.ExportDomainToCSharp(sessionId);
        await Assert.That(exportCs.Success).IsTrue();
        await Assert.That(ExtractCSharp(exportCs)!).Contains("WhenOrderActive");

        var exportDsl = DslTool.ExportDsl(sessionId);
        await Assert.That(exportDsl.Success).IsTrue();
        await Assert.That(ExtractPoly(exportDsl)).Contains("as order");
    }

    // ── Fail-closed: unbound peer path in apply_dsl ────────────

    [Test]
    public async Task ApplyDsl_UnboundPeerPathPrefix_FailsAnalysis() {
        var (sessionId, _) = SessionStore.Create("UnboundPeerPath");
        var bad = """
            domain UnboundPeerPath
            Tracker: entity {
              Status: Text
              Tracks: Order
              Pending: stage {
                when Tracks Active {
                  assign Status to order Code
                }
              }
            }
            Order: entity {
              Code: Text
              Draft: stage {}
              Active: stage {}
            }
            """;
        var apply = DslTool.ApplyDsl(sessionId, bad);
        // Apply may succeed evolve but analysis should report errors — or apply fails.
        // Product: SubscriptionAnalyzer fails closed on unbound peer-like root.
        if (apply.Success) {
            SessionStore.TryGet(sessionId, out var state);
            var analysis = DomainModelAnalyzer.Analyze(state!.Domain);
            await Assert.That(analysis.HasErrors || analysis.HasStructuralFailure).IsTrue();
        }
        else {
            await Assert.That(apply.Message.Length).IsGreaterThan(0);
        }
    }

    // ── create in + peer (product graph write) ─────────────────

    [Test]
    public async Task CreateIn_ThenActivate_EntityLevelPeer_CopiesCode_ViaMcp() {
        var (sessionId, _) = SessionStore.Create("CreateInWithPeer");
        var dsl = """
            domain CreateInWithPeer
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
            """;
        await Assert.That(DslTool.ApplyDsl(sessionId, dsl).Success).IsTrue();

        var custId = CreateAndId(sessionId, "Customer", """{"LastOrderCode":"NONE"}""");
        await Assert.That(RuntimeTool.InvokeAction(sessionId, custId, "PlaceOrder").Success)
            .IsTrue();

        SessionStore.TryGet(sessionId, out var st);
        var orderKvp = st!.InstanceMap.First(kvp => kvp.Value.Entity.Name == "Order");
        var orderId = orderKvp.Key;

        await Assert.That(RuntimeTool.InvokeAction(sessionId, orderId, "Activate").Success)
            .IsTrue();
        await Assert.That(GetProp(sessionId, custId, "LastOrderCode")).IsEqualTo("FROM-CREATE");
    }

    // ── unlink honesty via MCP ─────────────────────────────────

    [Test]
    public async Task Unlink_StopsPeerWhen_AndExistsFalse_ViaMcp() {
        var (sessionId, _) = SessionStore.Create("UnlinkStopsPeer");
        var dsl = """
            domain UnlinkStopsPeer
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
            """;
        await Assert.That(DslTool.ApplyDsl(sessionId, dsl).Success).IsTrue();

        var trackerId = CreateAndId(sessionId, "Tracker", """{"Status":"UNSET"}""");
        var orderId = CreateAndId(sessionId, "Order", """{"Code":"U-1"}""");
        await Assert.That(RuntimeTool.LinkInstances(sessionId, trackerId, "Tracks", orderId).Success)
            .IsTrue();

        var hasBefore = PolicyTool.EvaluatePolicy(sessionId, "Tracker", "HasOrder",
            instanceId: trackerId);
        await Assert.That(hasBefore.Message).Contains("true");

        await Assert.That(RuntimeTool.InvokeAction(sessionId, orderId, "Activate").Success)
            .IsTrue();
        await Assert.That(GetProp(sessionId, trackerId, "Status")).IsEqualTo("U-1");

        await Assert.That(RuntimeTool.UnlinkInstances(sessionId, trackerId, "Tracks", orderId)
            .Success).IsTrue();

        var hasAfter = PolicyTool.EvaluatePolicy(sessionId, "Tracker", "HasOrder",
            instanceId: trackerId);
        await Assert.That(hasAfter.Success).IsTrue();
        await Assert.That(hasAfter.Message).Contains("false");

        await Assert.That(RuntimeTool.InvokeAction(sessionId, orderId, "Reset").Success).IsTrue();
        // Direct prop write via store instance for isolation
        SessionStore.TryGet(sessionId, out var st);
        st!.InstanceMap[trackerId].SetProperty("Status", "AFTER-UNLINK");
        await Assert.That(RuntimeTool.InvokeAction(sessionId, orderId, "Activate").Success)
            .IsTrue();
        await Assert.That(GetProp(sessionId, trackerId, "Status")).IsEqualTo("AFTER-UNLINK");
    }

    // ── multi-subscriber ───────────────────────────────────────

    [Test]
    public async Task TwoTrackers_OneOrderActive_BothPeersFire_ViaMcp() {
        var (sessionId, _) = SessionStore.Create("TwoSubscribersOneOrder");
        var dsl = """
            domain TwoSubscribersOneOrder
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
            """;
        await Assert.That(DslTool.ApplyDsl(sessionId, dsl).Success).IsTrue();

        var t1 = CreateAndId(sessionId, "Tracker", """{"LastCode":"NONE"}""");
        var t2 = CreateAndId(sessionId, "Tracker", """{"LastCode":"NONE"}""");
        var orderId = CreateAndId(sessionId, "Order", """{"Code":"SHARED"}""");
        await Assert.That(RuntimeTool.LinkInstances(sessionId, t1, "Tracks", orderId).Success)
            .IsTrue();
        await Assert.That(RuntimeTool.LinkInstances(sessionId, t2, "Tracks", orderId).Success)
            .IsTrue();

        await Assert.That(RuntimeTool.InvokeAction(sessionId, orderId, "Activate").Success)
            .IsTrue();
        await Assert.That(GetProp(sessionId, t1, "LastCode")).IsEqualTo("SHARED");
        await Assert.That(GetProp(sessionId, t2, "LastCode")).IsEqualTo("SHARED");
    }

    // ── not Rel exists ─────────────────────────────────────────

    [Test]
    public async Task RelNotExists_BeforeAndAfterLink_ViaMcp() {
        var (sessionId, _) = SessionStore.Create("RelationshipNotExists");
        var dsl = """
            domain RelationshipNotExists
            Customer: entity {
              Name: Text
              profile: owned Profile
              NoProfile: policy { not profile exists }
            }
            Profile: entity {
              City: Text
            }
            """;
        await Assert.That(DslTool.ApplyDsl(sessionId, dsl).Success).IsTrue();

        var custId = CreateAndId(sessionId, "Customer", """{"Name":"A"}""");
        var before = PolicyTool.EvaluatePolicy(sessionId, "Customer", "NoProfile",
            instanceId: custId);
        await Assert.That(before.Success).IsTrue();
        await Assert.That(before.Message).Contains("true");

        var profileId = CreateAndId(sessionId, "Profile", """{"City":"X"}""");
        await Assert.That(RuntimeTool.LinkInstances(sessionId, custId, "profile", profileId)
            .Success).IsTrue();

        var after = PolicyTool.EvaluatePolicy(sessionId, "Customer", "NoProfile",
            instanceId: custId);
        await Assert.That(after.Success).IsTrue();
        await Assert.That(after.Message).Contains("false");
    }

    // ── multi-link path-prefix fails closed via MCP ────────────

    [Test]
    public async Task PathPrefix_MultipleLinks_EvaluateFailsClosed_ViaMcp() {
        var (sessionId, _) = SessionStore.Create("MultiLinkPathPrefix");
        // Use OneToOne profile but force two links if store allows — or many path-prefix.
        var dsl = """
            domain MultiLinkPathPrefix
            Customer: entity {
              Name: Text
              orders: many Order
              FirstCode: policy { orders Code is "X" }
            }
            Order: entity {
              Code: Text
            }
            """;
        var apply = DslTool.ApplyDsl(sessionId, dsl);
        // Analysis may reject many path-prefix at apply/analyze; either way is fail-closed.
        if (!apply.Success) {
            await Assert.That(apply.Success).IsFalse();
            return;
        }

        SessionStore.TryGet(sessionId, out var state);
        var analysis = DomainModelAnalyzer.Analyze(state!.Domain);
        if (analysis.HasErrors) {
            await Assert.That(analysis.HasErrors).IsTrue();
            return;
        }

        var custId = CreateAndId(sessionId, "Customer", """{"Name":"A"}""");
        var o1 = CreateAndId(sessionId, "Order", """{"Code":"X"}""");
        var o2 = CreateAndId(sessionId, "Order", """{"Code":"Y"}""");
        await Assert.That(RuntimeTool.LinkInstances(sessionId, custId, "orders", o1).Success)
            .IsTrue();
        await Assert.That(RuntimeTool.LinkInstances(sessionId, custId, "orders", o2).Success)
            .IsTrue();

        var eval = PolicyTool.EvaluatePolicy(sessionId, "Customer", "FirstCode",
            instanceId: custId);
        await Assert.That(eval.Success).IsFalse();
        await Assert.That(
            eval.Message.Contains("exactly one linked target", StringComparison.Ordinal)
            || eval.Message.Contains("Evaluation failed", StringComparison.Ordinal)).IsTrue();
    }

    // ── Quantifier empty-set honesty via MCP ───────────────────────────

    [Test]
    public async Task Quantifiers_EmptyLinks_Honesty_ViaMcp() {
        var (sessionId, _) = SessionStore.Create("EmptyQuantifiers");
        var dsl = """
            domain EmptyQuantifiers
            Customer: entity {
              Name: Text
              orders: many Order
              AnyBig: policy { any orders where Total > 100 }
              AllBig: policy { all orders where Total > 100 }
              NoneBig: policy { none orders where Total > 100 }
              OrderCountZero: policy { count orders is 0 }
            }
            Order: entity {
              Total: Number
            }
            """;
        await Assert.That(DslTool.ApplyDsl(sessionId, dsl).Success).IsTrue();
        var custId = CreateAndId(sessionId, "Customer", """{"Name":"A"}""");

        var any = PolicyTool.EvaluatePolicy(sessionId, "Customer", "AnyBig", instanceId: custId);
        await Assert.That(any.Success).IsTrue();
        await Assert.That(any.Message).Contains("false");

        var all = PolicyTool.EvaluatePolicy(sessionId, "Customer", "AllBig", instanceId: custId);
        await Assert.That(all.Success).IsTrue();
        await Assert.That(all.Message).Contains("false");

        var none = PolicyTool.EvaluatePolicy(sessionId, "Customer", "NoneBig", instanceId: custId);
        await Assert.That(none.Success).IsTrue();
        await Assert.That(none.Message).Contains("true");

        var count = PolicyTool.EvaluatePolicy(sessionId, "Customer", "OrderCountZero",
            instanceId: custId);
        await Assert.That(count.Success).IsTrue();
        await Assert.That(count.Message).Contains("true");
    }

    // ── require exists via MCP ─────────────────────────────────

    [Test]
    public async Task Require_HasOrders_BlocksThenPasses_ViaMcp() {
        var (sessionId, _) = SessionStore.Create("RequireRelationshipExists");
        var dsl = """
            domain RequireRelationshipExists
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
            """;
        await Assert.That(DslTool.ApplyDsl(sessionId, dsl).Success).IsTrue();
        var custId = CreateAndId(sessionId, "Customer", """{"Status":"open"}""");
        var orderId = CreateAndId(sessionId, "Order", """{"Code":"1"}""");

        var blocked = RuntimeTool.InvokeAction(sessionId, custId, "Ship");
        await Assert.That(blocked.Success).IsFalse();
        await Assert.That(GetProp(sessionId, custId, "Status")).IsEqualTo("open");

        await Assert.That(RuntimeTool.LinkInstances(sessionId, custId, "orders", orderId).Success)
            .IsTrue();
        var ok = RuntimeTool.InvokeAction(sessionId, custId, "Ship");
        await Assert.That(ok.Success).IsTrue();
        await Assert.That(GetProp(sessionId, custId, "Status")).IsEqualTo("shipped");
    }

    // ── if (Rel exists) via MCP ────────────────────────────────

    [Test]
    public async Task If_RelExists_Branches_ViaMcp() {
        var (sessionId, _) = SessionStore.Create("IfRelationshipExists");
        var dsl = """
            domain IfRelationshipExists
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
            """;
        await Assert.That(DslTool.ApplyDsl(sessionId, dsl).Success).IsTrue();
        var custId = CreateAndId(sessionId, "Customer", """{"Status":"?"}""");
        var orderId = CreateAndId(sessionId, "Order", """{"Code":"1"}""");

        await Assert.That(RuntimeTool.InvokeAction(sessionId, custId, "Mark").Success).IsTrue();
        await Assert.That(GetProp(sessionId, custId, "Status")).IsEqualTo("none");

        await Assert.That(RuntimeTool.LinkInstances(sessionId, custId, "orders", orderId).Success)
            .IsTrue();
        await Assert.That(RuntimeTool.InvokeAction(sessionId, custId, "Mark").Success).IsTrue();
        await Assert.That(GetProp(sessionId, custId, "Status")).IsEqualTo("has");
    }

    // ── multi-stage when via MCP ───────────────────────────────

    [Test]
    public async Task MultiStageWhen_Peer_FiresOnCompleted_ViaMcp() {
        var (sessionId, _) = SessionStore.Create("MultiStagePeerWhen");
        var dsl = """
            domain MultiStagePeerWhen
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
            """;
        await Assert.That(DslTool.ApplyDsl(sessionId, dsl).Success).IsTrue();
        var trackerId = CreateAndId(sessionId, "Tracker", """{"Status":"UNSET"}""");
        var orderId = CreateAndId(sessionId, "Order", """{"Code":"A"}""");
        await Assert.That(RuntimeTool.LinkInstances(sessionId, trackerId, "Tracks", orderId)
            .Success).IsTrue();

        await Assert.That(RuntimeTool.InvokeAction(sessionId, orderId, "Activate").Success)
            .IsTrue();
        await Assert.That(GetProp(sessionId, trackerId, "Status")).IsEqualTo("A");

        SessionStore.TryGet(sessionId, out var st);
        st!.InstanceMap[orderId].SetProperty("Code", "B");
        st.InstanceMap[trackerId].SetProperty("Status", "CLEARED");

        await Assert.That(RuntimeTool.InvokeAction(sessionId, orderId, "Complete").Success)
            .IsTrue();
        await Assert.That(GetProp(sessionId, trackerId, "Status")).IsEqualTo("B");
    }

    // ── wrong stage name / export dirty domain ─────────────────

    [Test]
    public async Task ApplyDsl_UnknownWhenStage_FailsAnalysis() {
        var (sessionId, _) = SessionStore.Create("UnknownWhenStage");
        var bad = """
            domain UnknownWhenStage
            Tracker: entity {
              Status: Text
              Tracks: Order
              Pending: stage {
                when Tracks NoSuchStage {
                  assign Status to "x"
                }
              }
            }
            Order: entity {
              Code: Text
              Draft: stage {}
              Active: stage {}
            }
            """;
        var apply = DslTool.ApplyDsl(sessionId, bad);
        if (apply.Success) {
            SessionStore.TryGet(sessionId, out var state);
            var analysis = DomainModelAnalyzer.Analyze(state!.Domain);
            await Assert.That(analysis.HasErrors || analysis.HasStructuralFailure).IsTrue();
        }
        else {
            await Assert.That(apply.Success).IsFalse();
        }
    }

    [Test]
    public async Task ExportDomainToCSharp_WithPeerAnalysisError_FailsClosed() {
        var (sessionId, _) = SessionStore.Create("PeerExportWithBindingError");
        // Force a domain that applies but has subscription binding errors if possible.
        // If apply rejects, export of empty/prior state is N/A — skip via assert on apply.
        var bad = """
            domain PeerExportBad
            Tracker: entity {
              Status: Text
              Tracks: Order
              Pending: stage {
                when Tracks Active {
                  assign Status to order Code
                }
              }
            }
            Order: entity {
              Code: Text
              Draft: stage {}
              Active: stage {}
            }
            """;
        var apply = DslTool.ApplyDsl(sessionId, bad);
        if (!apply.Success) {
            // Prefer evolve rejected at tool boundary
            await Assert.That(apply.Success).IsFalse();
            return;
        }

        SessionStore.TryGet(sessionId, out var state);
        var analysis = DomainModelAnalyzer.Analyze(state!.Domain);
        await Assert.That(analysis.HasErrors).IsTrue();

        var export = OracleTool.ExportDomainToCSharp(sessionId);
        // Export may use LatestAnalysis and throw or fail — must not silently emit peer handlers.
        if (export.Success) {
            var csharp = ExtractCSharp(export) ?? "";
            // Must not invent a clean peer param for an unbound body
            await Assert.That(
                csharp.Contains("WhenOrderActive") && csharp.Contains("Order order")).IsFalse();
        }
        else {
            await Assert.That(export.Success).IsFalse();
        }
    }

    // ── Helpers ────────────────────────────────────────────────

    private static string CreateAndId(string sessionId, string entity, string? props = null) {
        var create = RuntimeTool.CreateInstance(sessionId, entity, props);
        if (!create.Success)
            throw new InvalidOperationException($"create_instance {entity}: {create.Message}");
        var id = ExtractInstanceId(JsonSerializer.Serialize(create.Data));
        return id ?? throw new InvalidOperationException("missing instanceId");
    }

    private static string? ExtractInstanceId(string json) {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("instance", out var instance)
            && instance.TryGetProperty("instanceId", out var id))
            return id.GetString();
        if (doc.RootElement.TryGetProperty("instanceId", out var topId))
            return topId.GetString();
        return null;
    }

    private static string GetProp(string sessionId, string instanceId, string name) {
        SessionStore.TryGet(sessionId, out var st);
        var instance = st!.InstanceMap[instanceId];
        return instance.Snapshot().TryGetValue(name, out var v) ? v?.ToString() ?? "(null)" : "(missing)";
    }

    private static string? GetStage(string sessionId, string instanceId) {
        SessionStore.TryGet(sessionId, out var st);
        return st!.InstanceMap[instanceId].CurrentStage;
    }

    private static string ExtractPoly(DomainToolResponse export) {
        var prop = export.Data!.GetType().GetProperty("poly");
        return prop?.GetValue(export.Data) as string ?? "";
    }

    private static string? ExtractCSharp(DomainToolResponse export) {
        var prop = export.Data!.GetType().GetProperty("csharp");
        return prop?.GetValue(export.Data) as string;
    }
}