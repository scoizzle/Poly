using Poly.DomainModeling.Runtime;
using Poly.Mcp.Sessions;

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
        var create = McpTestTools.Surface.Create(sessionId, entity, props);
        if (!create.Ok)
            throw new InvalidOperationException($"create_instance {entity}: {create.Summary}");
        return create.InstanceId;
    }

    private static int ListFineCount(string sessionId) {
        var list = McpTestTools.Surface.List(sessionId, "Fine");
        if (!list.Ok)
            throw new InvalidOperationException(list.Summary);
        return list.Data!["count"]!.GetValue<int>();
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
        var has = McpTestTools.Surface.Evaluate(sessionId, "Patron", "HasFines", patronId);
        await Assert.That(has.Ok).IsTrue();
        await Assert.That(has.Summary).Contains(hasFines ? "true" : "false");

        var count = McpTestTools.Surface.Evaluate(sessionId, "Patron", "HasFineCount", patronId);
        await Assert.That(count.Ok).IsTrue();
        await Assert.That(count.Summary).Contains(hasFines ? "true" : "false");

        var none = McpTestTools.Surface.Evaluate(sessionId, "Patron", "NoFines", patronId);
        await Assert.That(none.Ok).IsTrue();
        await Assert.That(none.Summary).Contains(noFines ? "true" : "false");
    }

    [Test]
    public async Task TypeOnly_UnambiguousManyRel_ListsAndLinks() {
        // Bare create Type lists Fine; Patron.fines and Fine.patron stay unlinked.
        var sessionId = McpTestTools.Surface.Open("SimulateCreateType");
        await Assert.That(McpTestTools.Surface.Define(sessionId, ReadProbe("simulate-create-type.poly")).Success)
            .IsTrue();

        var patronId = CreateAndId(sessionId, "Patron", """{"Name":"Ada"}""");
        var invoke = McpTestTools.Surface.Invoke(sessionId, patronId, "AssessByType");
        await Assert.That(invoke.Ok).IsTrue();
        var fineId = invoke.Result.ReturnInstanceId;
        await Assert.That(fineId).IsNotNull();

        await Assert.That(ListFineCount(sessionId)).IsEqualTo(1);
        await AssertPolicies(sessionId, patronId, hasFines: false, noFines: true);

        var (patron, fine, store) = ResolvePatronFine(sessionId, patronId, fineId);
        await Assert.That(store.GetRelatedInstances("fines", patron).Count).IsEqualTo(0);
        await Assert.That(store.GetRelatedInstances("patron", fine!).Count).IsEqualTo(0);
    }

    [Test]
    public async Task RelOnly_CreateIn_ListsAndLinksBothDirections() {
        var sessionId = McpTestTools.Surface.Open("SimulateCreateIn");
        await Assert.That(McpTestTools.Surface.Define(sessionId, ReadProbe("simulate-create-in.poly")).Success)
            .IsTrue();

        var patronId = CreateAndId(sessionId, "Patron", """{"Name":"Bea"}""");
        var invoke = McpTestTools.Surface.Invoke(sessionId, patronId, "AssessByRel");
        await Assert.That(invoke.Ok).IsTrue();
        var fineId = invoke.Result.ReturnInstanceId;
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
        var sessionId = McpTestTools.Surface.Open("SimulateCreateCreateIn");
        await Assert.That(McpTestTools.Surface.Define(sessionId, ReadProbe("simulate-create-create-in.poly")).Success)
            .IsTrue();

        var patronId = CreateAndId(sessionId, "Patron", """{"Name":"Cara"}""");

        var typeInvoke = McpTestTools.Surface.Invoke(sessionId, patronId, "AssessByType");
        await Assert.That(typeInvoke.Ok).IsTrue();
        var fineTypeId = typeInvoke.Result.ReturnInstanceId;
        await Assert.That(fineTypeId).IsNotNull();

        await Assert.That(ListFineCount(sessionId)).IsEqualTo(1);
        await AssertPolicies(sessionId, patronId, hasFines: false, noFines: true);
        var afterType = ResolvePatronFine(sessionId, patronId, fineTypeId);
        await Assert.That(afterType.Store.GetRelatedInstances("fines", afterType.Patron).Count)
            .IsEqualTo(0);
        await Assert.That(afterType.Store.GetRelatedInstances("patron", afterType.Fine!).Count)
            .IsEqualTo(0);

        var relInvoke = McpTestTools.Surface.Invoke(sessionId, patronId, "AssessByRel");
        await Assert.That(relInvoke.Ok).IsTrue();
        var fineRelId = relInvoke.Result.ReturnInstanceId;
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
        var sessionId = McpTestTools.Surface.Open("SimulateCreateAmbiguous");
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
        var applied = McpTestTools.Surface.Define(sessionId, dsl);
        await Assert.That(applied.Success).IsFalse();
        var findings = applied.Findings;
        await Assert.That(findings.Count(static d => d.Code == "DMREL005")).IsEqualTo(1);
        var finding = findings.First(static d => d.Code == "DMREL005").Message;
        await Assert.That(finding).Contains("'fines'");
        await Assert.That(finding).Contains("'waived'");
    }

    [Test]
    public async Task ActionResultCreate_OmittedCollection_WithActionParameter_Succeeds() {
        var sessionId = McpTestTools.Surface.Open("SimulateCreateOmittedCollection");
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
        await Assert.That(McpTestTools.Surface.Define(sessionId, dsl).Success).IsTrue();
        var clerkId = CreateAndId(sessionId, "Clerk", """{"Name":"Ada"}""");
        var invoke = McpTestTools.Surface.Invoke(sessionId, clerkId, "Hire", """{"name":"Ada"}""");
        await Assert.That(invoke.Ok).IsTrue();
        var patronId = invoke.Result.ReturnInstanceId;
        await Assert.That(patronId).IsNotNull();
        if (!McpTestTools.Store.TryGet(sessionId, out var state) || state is null)
            throw new InvalidOperationException("session missing");
        var patron = Find(state, patronId!);
        await Assert.That(patron.Bag["Name"]).IsEqualTo("Ada");
        await Assert.That(new LinkView(state.Executor).GetRelatedInstances("fines", patron).Count).IsEqualTo(0);
    }

    [Test]
    public async Task ActionResultCreate_OmittedSingularNav_WithActionParameter_Succeeds() {
        var sessionId = McpTestTools.Surface.Open("SimulateCreateOmittedNav");
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
              patron: Patron optional
            }

            Patron: entity {
              Name: Text
            }
            """;
        await Assert.That(McpTestTools.Surface.Define(sessionId, dsl).Success).IsTrue();
        var deskId = CreateAndId(sessionId, "Desk", """{"Name":"Front"}""");
        var invoke = McpTestTools.Surface.Invoke(sessionId, deskId, "CheckOut", """{"reason":"Overdue"}""");
        await Assert.That(invoke.Ok).IsTrue();
        var fineId = invoke.Result.ReturnInstanceId;
        await Assert.That(fineId).IsNotNull();
        if (!McpTestTools.Store.TryGet(sessionId, out var state) || state is null)
            throw new InvalidOperationException("session missing");
        var fine = Find(state, fineId!);
        await Assert.That(fine.Bag["Reason"]).IsEqualTo("Overdue");
        await Assert.That(new LinkView(state.Executor).GetRelatedInstances("patron", fine).Count).IsEqualTo(0);
    }

    [Test]
    public async Task ActionResultCreate_OmittedRuntimeDefault_StoresClock() {
        var sessionId = McpTestTools.Surface.Open("SimulateCreateOmittedClock");
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
        await Assert.That(McpTestTools.Surface.Define(sessionId, dsl).Success).IsTrue();
        var clerkId = CreateAndId(sessionId, "Clerk", """{"Name":"Ada"}""");
        var invoke = McpTestTools.Surface.Invoke(sessionId, clerkId, "Open", """{"name":"Ada"}""");
        await Assert.That(invoke.Ok).IsTrue();
        var accountId = invoke.Result.ReturnInstanceId;
        await Assert.That(accountId).IsNotNull();
        if (!McpTestTools.Store.TryGet(sessionId, out var state) || state is null)
            throw new InvalidOperationException("session missing");
        var account = state.InstanceMap[accountId!];
        await Assert.That((DateOnly)account["Opened"]!)
            .IsEqualTo(DateOnly.FromDateTime(DateTime.UtcNow));
    }
}