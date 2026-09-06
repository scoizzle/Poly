using System.Text.Json;

using Poly.DomainModeling.Runtime;
using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// Fail-closed MCP simulate for Fine Type-create vs create-in (PR 52).
/// apply_dsl → create_instance → invoke_action → list_instances / evaluate_policy /
/// store GetRelatedInstances + returnInstanceId. Type-create auto-links when the
/// source owns an unambiguous many-rel to Fine (closes list-vs-policy skew).
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
        var list = RuntimeTool.ListInstances(sessionId, entityName: "Fine");
        if (!list.Success)
            throw new InvalidOperationException(list.Message);
        var json = JsonSerializer.Serialize(list.Data);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("count").GetInt32();
    }

    private static (DomainEntityInstance Patron, DomainEntityInstance? Fine, DomainInstanceStore Store)
        ResolvePatronFine(string sessionId, string patronId, string? fineId) {
        if (!McpSessionStore.TryGet(sessionId, out var state) || state is null)
            throw new InvalidOperationException("session missing");
        if (state.InstanceStore is null)
            throw new InvalidOperationException("store missing");
        var patron = state.InstanceMap[patronId];
        DomainEntityInstance? fine = fineId is null ? null : state.InstanceMap[fineId];
        return (patron, fine, state.InstanceStore);
    }

    private static async Task AssertPolicies(
        string sessionId, string patronId, bool hasFines, bool noFines) {
        var has = PolicyTool.EvaluatePolicy(sessionId, "Patron", "HasFines", patronId);
        await Assert.That(has.Success).IsTrue();
        await Assert.That(has.Message).Contains(hasFines ? "true" : "false");

        var count = PolicyTool.EvaluatePolicy(sessionId, "Patron", "HasFineCount", patronId);
        await Assert.That(count.Success).IsTrue();
        await Assert.That(count.Message).Contains(hasFines ? "true" : "false");

        var none = PolicyTool.EvaluatePolicy(sessionId, "Patron", "NoFines", patronId);
        await Assert.That(none.Success).IsTrue();
        await Assert.That(none.Message).Contains(noFines ? "true" : "false");
    }

    [Test]
    public async Task TypeOnly_UnambiguousManyRel_ListsAndLinks() {
        var (sessionId, _) = McpSessionStore.Create("SimulateCreateType");
        await Assert.That(DslTool.ApplyDsl(sessionId, ReadProbe("simulate-create-type.poly")).Success)
            .IsTrue();

        var patronId = CreateAndId(sessionId, "Patron", """{"Name":"Ada"}""");
        var invoke = RuntimeTool.InvokeAction(sessionId, patronId, "AssessByType");
        await Assert.That(invoke.Success).IsTrue();
        var fineId = ExtractReturnInstanceId(invoke);
        await Assert.That(fineId).IsNotNull();

        await Assert.That(ListFineCount(sessionId)).IsEqualTo(1);
        await AssertPolicies(sessionId, patronId, hasFines: true, noFines: false);

        var (patron, fine, store) = ResolvePatronFine(sessionId, patronId, fineId);
        await Assert.That(store.GetRelatedInstances("fines", patron).Count).IsEqualTo(1);
        await Assert.That(store.GetRelatedInstances("patron", fine!).Count).IsEqualTo(1);
        await Assert.That(store.GetRelatedInstances("patron", fine!).Single())
            .IsEqualTo(patron);
    }

    [Test]
    public async Task RelOnly_CreateIn_ListsAndLinksBothDirections() {
        var (sessionId, _) = McpSessionStore.Create("SimulateCreateIn");
        await Assert.That(DslTool.ApplyDsl(sessionId, ReadProbe("simulate-create-in.poly")).Success)
            .IsTrue();

        var patronId = CreateAndId(sessionId, "Patron", """{"Name":"Bea"}""");
        var invoke = RuntimeTool.InvokeAction(sessionId, patronId, "AssessByRel");
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
        var (sessionId, _) = McpSessionStore.Create("SimulateCreateCreateIn");
        await Assert.That(DslTool.ApplyDsl(sessionId, ReadProbe("simulate-create-create-in.poly")).Success)
            .IsTrue();

        var patronId = CreateAndId(sessionId, "Patron", """{"Name":"Cara"}""");

        var typeInvoke = RuntimeTool.InvokeAction(sessionId, patronId, "AssessByType");
        await Assert.That(typeInvoke.Success).IsTrue();
        var fineTypeId = ExtractReturnInstanceId(typeInvoke);
        await Assert.That(fineTypeId).IsNotNull();

        await Assert.That(ListFineCount(sessionId)).IsEqualTo(1);
        await AssertPolicies(sessionId, patronId, hasFines: true, noFines: false);
        var afterType = ResolvePatronFine(sessionId, patronId, fineTypeId);
        await Assert.That(afterType.Store.GetRelatedInstances("fines", afterType.Patron).Count)
            .IsEqualTo(1);
        await Assert.That(afterType.Store.GetRelatedInstances("patron", afterType.Fine!).Count)
            .IsEqualTo(1);

        var relInvoke = RuntimeTool.InvokeAction(sessionId, patronId, "AssessByRel");
        await Assert.That(relInvoke.Success).IsTrue();
        var fineRelId = ExtractReturnInstanceId(relInvoke);
        await Assert.That(fineRelId).IsNotNull();
        await Assert.That(fineRelId).IsNotEqualTo(fineTypeId);

        await Assert.That(ListFineCount(sessionId)).IsEqualTo(2);
        await AssertPolicies(sessionId, patronId, hasFines: true, noFines: false);

        if (!McpSessionStore.TryGet(sessionId, out var state) || state?.InstanceStore is null)
            throw new InvalidOperationException("session/store missing");
        var patron = state.InstanceMap[patronId];
        var fineType = state.InstanceMap[fineTypeId!];
        var fineRel = state.InstanceMap[fineRelId!];
        await Assert.That(state.InstanceStore.GetRelatedInstances("fines", patron).Count)
            .IsEqualTo(2);
        await Assert.That(state.InstanceStore.GetRelatedInstances("patron", fineType).Count)
            .IsEqualTo(1);
        await Assert.That(state.InstanceStore.GetRelatedInstances("patron", fineRel).Count)
            .IsEqualTo(1);
    }
}
