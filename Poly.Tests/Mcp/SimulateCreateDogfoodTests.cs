using System.Text.Json;

using Poly.DomainModeling.Runtime;
using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// Fail-closed MCP simulate for Fine Type-create vs create-in.
/// apply_dsl → create_instance → invoke_action → list_instances / evaluate_policy /
/// store GetRelatedInstances + returnInstanceId. Type-create lists Fine unlinked;
/// create-in links outbound and the unambiguous reverse.
/// </summary>
public class SimulateCreateDogfoodTests {
    private static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir, "Poly.sln"))
                || File.Exists(Path.Combine(dir, "docs/CORE.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root from " + AppContext.BaseDirectory);
    }

    private static string ReadProbe(string relativeUnderDogfood) {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, "docs/probes/dogfood", relativeUnderDogfood));
    }

    private static string CreateAndId(string sessionId, string entity, string? props = null) {
        var create = McpTestTools.Runtime.CreateInstance(sessionId, entity, props);
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

    private static string? ExtractReturnInstanceId(DomainToolResponse invoke) {
        if (invoke.Data is null) return null;
        var json = JsonSerializer.Serialize(invoke.Data);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("invokeActionResult", out var result)
            && result.TryGetProperty("returnInstanceId", out var id))
            return id.GetString();
        if (doc.RootElement.TryGetProperty("returnInstanceId", out var top))
            return top.GetString();
        return null;
    }

    private static int ListFineCount(string sessionId) {
        var list = McpTestTools.Runtime.ListInstances(sessionId, entityName: "Fine");
        if (!list.Success)
            throw new InvalidOperationException(list.Message);
        var json = JsonSerializer.Serialize(list.Data);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("count").GetInt32();
    }

    /// <summary>Links either direction by relationship name, read from the executor's bags.</summary>
    private sealed class LinkView(ModuleInstanceExecutor executor) {
        public IReadOnlyList<ModuleInstance> GetRelatedInstances(string relationship, ModuleInstance instance) =>
            [.. executor.LinkedTargets(relationship, instance), .. executor.LinkedSources(relationship, instance)];
    }

    private static ModuleInstance Find(McpSessionState state, string id) =>
        state.Executor.Instances.Single(i => i.Id == id);

    private static (ModuleInstance Patron, ModuleInstance? Fine, LinkView Store)
        ResolvePatronFine(string sessionId, string patronId, string? fineId) {
        if (!McpTestTools.Store.TryGet(sessionId, out var state) || state is null)
            throw new InvalidOperationException("session missing");
        var patron = Find(state, patronId);
        ModuleInstance? fine = fineId is null ? null : Find(state, fineId);
        return (patron, fine, new LinkView(state.Executor));
    }

    private static async Task AssertPolicies(
        string sessionId, string patronId, bool hasFines, bool noFines) {
        var has = McpTestTools.Policy.EvaluatePolicy(sessionId, "Patron", "HasFines", patronId);
        await Assert.That(has.Success).IsTrue();
        await Assert.That(has.Message).Contains(hasFines ? "true" : "false");

        var count = McpTestTools.Policy.EvaluatePolicy(sessionId, "Patron", "HasFineCount", patronId);
        await Assert.That(count.Success).IsTrue();
        await Assert.That(count.Message).Contains(hasFines ? "true" : "false");

        var none = McpTestTools.Policy.EvaluatePolicy(sessionId, "Patron", "NoFines", patronId);
        await Assert.That(none.Success).IsTrue();
        await Assert.That(none.Message).Contains(noFines ? "true" : "false");
    }

    [Test]
    public async Task TypeOnly_UnambiguousManyRel_ListsAndLinks() {
        // Bare create Type lists Fine; Patron.fines and Fine.patron stay unlinked.
        var (sessionId, _) = McpTestTools.Store.Create("SimulateCreateType");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, ReadProbe("simulate-create-type.poly")).Success)
            .IsTrue();

        var patronId = CreateAndId(sessionId, "Patron", """{"Name":"Ada"}""");
        var invoke = McpTestTools.Runtime.InvokeAction(sessionId, patronId, "AssessByType");
        await Assert.That(invoke.Success).IsTrue();
        var fineId = ExtractReturnInstanceId(invoke);
        await Assert.That(fineId).IsNotNull();

        await Assert.That(ListFineCount(sessionId)).IsEqualTo(1);
        await AssertPolicies(sessionId, patronId, hasFines: false, noFines: true);

        var (patron, fine, store) = ResolvePatronFine(sessionId, patronId, fineId);
        await Assert.That(store.GetRelatedInstances("fines", patron).Count).IsEqualTo(0);
        await Assert.That(store.GetRelatedInstances("patron", fine!).Count).IsEqualTo(0);
    }

    [Test]
    public async Task RelOnly_CreateIn_ListsAndLinksBothDirections() {
        var (sessionId, _) = McpTestTools.Store.Create("SimulateCreateIn");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, ReadProbe("simulate-create-in.poly")).Success)
            .IsTrue();

        var patronId = CreateAndId(sessionId, "Patron", """{"Name":"Bea"}""");
        var invoke = McpTestTools.Runtime.InvokeAction(sessionId, patronId, "AssessByRel");
        await Assert.That(invoke.Success).IsTrue();
        var fineId = ExtractReturnInstanceId(invoke);
        await Assert.That(fineId).IsNotNull();

        await Assert.That(ListFineCount(sessionId)).IsEqualTo(1);
        await AssertPolicies(sessionId, patronId, hasFines: true, noFines: false);

        var (patron, fine, store) = ResolvePatronFine(sessionId, patronId, fineId);
        await Assert.That(store.GetRelatedInstances("fines", patron).Count).IsEqualTo(1);
        await Assert.That(store.GetRelatedInstances("patron", fine!).Count).IsEqualTo(1);
    }

    [Test]
    public async Task Combined_TypeThenRel_OnOnePatron_BothLinked() {
        // Type Fine unlinked; Rel Fine linked both ways. list=2, fines links=1.
        var (sessionId, _) = McpTestTools.Store.Create("SimulateCreateCreateIn");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, ReadProbe("simulate-create-create-in.poly")).Success)
            .IsTrue();

        var patronId = CreateAndId(sessionId, "Patron", """{"Name":"Cara"}""");

        var typeInvoke = McpTestTools.Runtime.InvokeAction(sessionId, patronId, "AssessByType");
        await Assert.That(typeInvoke.Success).IsTrue();
        var fineTypeId = ExtractReturnInstanceId(typeInvoke);
        await Assert.That(fineTypeId).IsNotNull();

        await Assert.That(ListFineCount(sessionId)).IsEqualTo(1);
        await AssertPolicies(sessionId, patronId, hasFines: false, noFines: true);
        var afterType = ResolvePatronFine(sessionId, patronId, fineTypeId);
        await Assert.That(afterType.Store.GetRelatedInstances("fines", afterType.Patron).Count)
            .IsEqualTo(0);
        await Assert.That(afterType.Store.GetRelatedInstances("patron", afterType.Fine!).Count)
            .IsEqualTo(0);

        var relInvoke = McpTestTools.Runtime.InvokeAction(sessionId, patronId, "AssessByRel");
        await Assert.That(relInvoke.Success).IsTrue();
        var fineRelId = ExtractReturnInstanceId(relInvoke);
        await Assert.That(fineRelId).IsNotNull();
        await Assert.That(fineRelId).IsNotEqualTo(fineTypeId);

        await Assert.That(ListFineCount(sessionId)).IsEqualTo(2);
        await AssertPolicies(sessionId, patronId, hasFines: true, noFines: false);

        if (!McpTestTools.Store.TryGet(sessionId, out var state) || state is null)
            throw new InvalidOperationException("session missing");
        var links = new LinkView(state.Executor);
        var patron = Find(state, patronId);
        var fineType = Find(state, fineTypeId!);
        var fineRel = Find(state, fineRelId!);
        await Assert.That(links.GetRelatedInstances("fines", patron).Count)
            .IsEqualTo(1);
        await Assert.That(links.GetRelatedInstances("patron", fineType).Count)
            .IsEqualTo(0);
        await Assert.That(links.GetRelatedInstances("patron", fineRel).Count)
            .IsEqualTo(1);
    }

    [Test]
    public async Task TypeOnly_AmbiguousManyRel_RejectedByApplyDsl() {
        // Flip (Q10, #659): S-11 makes the undeclared ambiguous pairing an error, so
        // apply_dsl refuses the document. The old assertion applied it and ran the
        // create: the Fine was listed under neither collection ("lists but does not link").
        var (sessionId, _) = McpTestTools.Store.Create("SimulateCreateAmbiguous");
        var dsl = """
            domain SimulateCreateAmbiguous
            uses temporal
            uses storage

            Patron: entity {
              Name: Text
              fines: many Fine
              waived: many Fine
              HasFines: policy { fines exists }
              HasFineCount: policy { count fines > 0 }
              NoFines: policy { count fines == 0 }

              Active: stage {
                AssessByType: action -> Fine {
                  create Fine { Amount: 5 Reason: "Ambiguous" }
                }
              }
            }

            Fine: entity {
              Amount: Number range(0, 500)
              Reason: Text
              patron: Patron
            }
            """;
        var applied = McpTestTools.Dsl.ApplyDsl(sessionId, dsl);
        await Assert.That(applied.Success).IsFalse();
        var findings = applied.Diagnostics ?? [];
        await Assert.That(findings.Count(static d => d.Contains("DMREL005"))).IsEqualTo(1);
        var finding = findings.First(static d => d.Contains("DMREL005"));
        await Assert.That(finding).Contains("'fines'");
        await Assert.That(finding).Contains("'waived'");
    }

    [Test]
    public async Task ActionResultCreate_OmittedCollection_WithActionParameter_Succeeds() {
        var (sessionId, _) = McpTestTools.Store.Create("SimulateCreateOmittedCollection");
        var dsl = """
            domain SimulateCreateOmittedCollection
            uses storage

            Clerk: entity {
              Name: Text
              Active: stage {
                Hire: action (name: Text) -> Patron {
                  create Patron { Name: name }
                }
              }
            }

            Patron: entity {
              Name: Text
              fines: many Fine
            }

            Fine: entity {
              Amount: Number
              Reason: Text
            }
            """;
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, dsl).Success).IsTrue();
        var clerkId = CreateAndId(sessionId, "Clerk", """{"Name":"Ada"}""");
        var invoke = McpTestTools.Runtime.InvokeAction(sessionId, clerkId, "Hire", """{"name":"Ada"}""");
        await Assert.That(invoke.Success).IsTrue();
        var patronId = ExtractReturnInstanceId(invoke);
        await Assert.That(patronId).IsNotNull();
        if (!McpTestTools.Store.TryGet(sessionId, out var state) || state is null)
            throw new InvalidOperationException("session missing");
        var patron = Find(state, patronId!);
        await Assert.That(patron.Bag["Name"]).IsEqualTo("Ada");
        await Assert.That(new LinkView(state.Executor).GetRelatedInstances("fines", patron).Count).IsEqualTo(0);
    }

    [Test]
    public async Task ActionResultCreate_OmittedSingularNav_WithActionParameter_Succeeds() {
        var (sessionId, _) = McpTestTools.Store.Create("SimulateCreateOmittedNav");
        var dsl = """
            domain SimulateCreateOmittedNav
            uses storage

            Desk: entity {
              Name: Text
              Active: stage {
                CheckOut: action (reason: Text) -> Fine {
                  create Fine { Amount: 1 Reason: reason }
                }
              }
            }

            Fine: entity {
              Amount: Number
              Reason: Text
              patron: Patron
            }

            Patron: entity {
              Name: Text
            }
            """;
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, dsl).Success).IsTrue();
        var deskId = CreateAndId(sessionId, "Desk", """{"Name":"Front"}""");
        var invoke = McpTestTools.Runtime.InvokeAction(sessionId, deskId, "CheckOut", """{"reason":"Overdue"}""");
        await Assert.That(invoke.Success).IsTrue();
        var fineId = ExtractReturnInstanceId(invoke);
        await Assert.That(fineId).IsNotNull();
        if (!McpTestTools.Store.TryGet(sessionId, out var state) || state is null)
            throw new InvalidOperationException("session missing");
        var fine = Find(state, fineId!);
        await Assert.That(fine.Bag["Reason"]).IsEqualTo("Overdue");
        await Assert.That(new LinkView(state.Executor).GetRelatedInstances("patron", fine).Count).IsEqualTo(0);
    }

    [Test]
    public async Task ActionResultCreate_OmittedRuntimeDefault_StoresClock() {
        var (sessionId, _) = McpTestTools.Store.Create("SimulateCreateOmittedClock");
        var dsl = """
            domain SimulateCreateOmittedClock
            uses temporal
            uses storage

            Clerk: entity {
              Name: Text
              Active: stage {
                Open: action (name: Text) -> Account {
                  create Account { Name: name }
                }
              }
            }

            Account: entity {
              Name: Text
              Opened: Date default(Today)
            }
            """;
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, dsl).Success).IsTrue();
        var clerkId = CreateAndId(sessionId, "Clerk", """{"Name":"Ada"}""");
        var invoke = McpTestTools.Runtime.InvokeAction(sessionId, clerkId, "Open", """{"name":"Ada"}""");
        await Assert.That(invoke.Success).IsTrue();
        var accountId = ExtractReturnInstanceId(invoke);
        await Assert.That(accountId).IsNotNull();
        if (!McpTestTools.Store.TryGet(sessionId, out var state) || state is null)
            throw new InvalidOperationException("session missing");
        var account = state.InstanceMap[accountId!];
        await Assert.That((DateOnly)account["Opened"]!)
            .IsEqualTo(DateOnly.FromDateTime(DateTime.UtcNow));
    }
}