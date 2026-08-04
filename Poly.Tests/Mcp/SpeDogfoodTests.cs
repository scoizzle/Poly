using System.Text.Json;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// SPE dogfood — agent-facing MCP path for shipped surface extensions:
/// export peer (E), entity-level when + peer binder (L), owned path-prefix (O),
/// store-aware Rel exists. Prefer these over unit goldens when proving product honesty.
/// </summary>
public class SpeDogfoodTests {
    /// <summary>
    /// Shared SpeDogfood domain: entity-level peer when, stage-scoped notification when,
    /// owned profile policies, Rel exists. Used by multiple goldens.
    /// </summary>
    private const string SpeDogfoodDomain = """
        domain SpeDogfood

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
    public async Task ApplyDsl_SpeSurface_AnalyzesClean() {
        var (sessionId, _) = McpSessionStore.Create("SpeDogfood");
        var apply = DslTool.ApplyDsl(sessionId, SpeDogfoodDomain);
        await Assert.That(apply.Success).IsTrue();

        McpSessionStore.TryGet(sessionId, out var state);
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
    public async Task ExportDsl_SpeSurface_RoundTripsPeerEntityWhenAndOwned() {
        var (sessionId, _) = McpSessionStore.Create("SpeDogfood");
        await Assert.That(DslTool.ApplyDsl(sessionId, SpeDogfoodDomain).Success).IsTrue();

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
        McpSessionStore.TryGet(sessionId, out var state);
        var customer = state!.Domain.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        await Assert.That(customer.Subscriptions[0].PeerBinding).IsEqualTo("order");
    }

    // ── L: entity-level when + peer binder (MCP invoke path) ───

    [Test]
    public async Task EntityLevel_PeerWhen_CopiesPeerCode_RegardlessOfSubscriberStage() {
        // SPE-L: entity-level when fires in Idle (not Pending); peer binder copies order Code.
        var (sessionId, _) = McpSessionStore.Create("SpeDogfood");
        await Assert.That(DslTool.ApplyDsl(sessionId, SpeDogfoodDomain).Success).IsTrue();

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
        var (sessionId, _) = McpSessionStore.Create("SpeStageContrast");
        var dsl = """
            domain SpeStageContrast
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
        var (sessionId, _) = McpSessionStore.Create("SpeStagePeer");
        var dsl = """
            domain SpeStagePeer
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
        var (sessionId, _) = McpSessionStore.Create("SpeDogfood");
        await Assert.That(DslTool.ApplyDsl(sessionId, SpeDogfoodDomain).Success).IsTrue();

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
        var (sessionId, _) = McpSessionStore.Create("SpeDogfood");
        await Assert.That(DslTool.ApplyDsl(sessionId, SpeDogfoodDomain).Success).IsTrue();

        var custId = CreateAndId(sessionId, "Customer",
            """{"Name":"Alone","Status":"x","LastOrderCode":"n"}""");

        var eval = PolicyTool.EvaluatePolicy(sessionId, "Customer", "IsUrban", instanceId: custId);
        await Assert.That(eval.Success).IsFalse();
    }

    // ── Rel exists (post-SPE store-aware) ───────────────────────

    [Test]
    public async Task RelExists_LinkedAndUnlinked_EvaluatesHonestly() {
        var (sessionId, _) = McpSessionStore.Create("SpeDogfood");
        await Assert.That(DslTool.ApplyDsl(sessionId, SpeDogfoodDomain).Success).IsTrue();

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
        var (sessionId, _) = McpSessionStore.Create("SpeDogfood");
        await Assert.That(DslTool.ApplyDsl(sessionId, SpeDogfoodDomain).Success).IsTrue();

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
        var (sessionId, _) = McpSessionStore.Create("SpeExport");
        var dsl = """
            domain SpeExport
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
        var (sessionId, _) = McpSessionStore.Create("SpeDogfood");
        await Assert.That(DslTool.ApplyDsl(sessionId, SpeDogfoodDomain).Success).IsTrue();

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
        var (sessionId, _) = McpSessionStore.Create("SpeDogfood");
        await Assert.That(DslTool.ApplyDsl(sessionId, SpeDogfoodDomain).Success).IsTrue();

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
        var (sessionId, _) = McpSessionStore.Create("SpeFailClosed");
        var bad = """
            domain SpeFailClosed
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
            McpSessionStore.TryGet(sessionId, out var state);
            var analysis = DomainModelAnalyzer.Analyze(state!.Domain);
            await Assert.That(analysis.HasErrors || analysis.HasStructuralFailure).IsTrue();
        }
        else {
            await Assert.That(apply.Message.Length).IsGreaterThan(0);
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
        McpSessionStore.TryGet(sessionId, out var st);
        var instance = st!.InstanceMap[instanceId];
        return instance.Snapshot().TryGetValue(name, out var v) ? v?.ToString() ?? "(null)" : "(missing)";
    }

    private static string? GetStage(string sessionId, string instanceId) {
        McpSessionStore.TryGet(sessionId, out var st);
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