using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// #464: <c>invoke_action</c> resolves the action by the stage the instance was in when
/// it was invoked, so two stages that share an action name each report their own result.
/// </summary>
public class RuntimeStageActionResolutionTests {
    // Two stages share "Cancel"; only Active's is gated by "require not Ready". (A same-named
    // stage action whose declared result differs from its sibling's crashes lowering today —
    // a separate defect — so the bodies here differ by their guard instead.)
    private const string Dsl = """
        domain Stages
        Order: entity {
          Memo: Text
          Draft: stage {
            Cancel: action { transition to Closed }
            Activate: action { transition to Active }
          }
          Active: stage {
            Cancel: action require not Ready { transition to Closed }
          }
          Closed: stage {
            Reopen: action { transition to Draft }
          }
          Ready: policy { Memo is "m" }
        }
        """;

    private static string NewOrder(string sessionId) {
        var create = McpTestTools.Runtime.CreateInstance(sessionId, "Order", """{"Memo":"m"}""");
        if (!create.Success)
            throw new InvalidOperationException(create.Message);
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(create.Data));
        return doc.RootElement.GetProperty("instance").GetProperty("instanceId").GetString()!;
    }

    private sealed record Outcome(bool Succeeded, string? ErrorMessage, string? NewStage, IReadOnlyList<string> FailedGuards);

    private static Outcome Result(DomainToolResponse response) {
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(response.Data));
        var r = doc.RootElement.GetProperty("invokeActionResult");
        string? Str(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
            ? v.GetString() : null;
        return new Outcome(r.GetProperty("succeeded").GetBoolean(), Str("errorMessage"), Str("newStage"),
            r.TryGetProperty("failedGuards", out var g) && g.ValueKind == System.Text.Json.JsonValueKind.Array
                ? g.EnumerateArray().Select(x => x.GetString()!).ToList() : []);
    }

    [Test]
    public async Task InvokeAction_FromActiveStage_MapsGuardsOfActiveCancelNotDraftCancel() {
        var (sessionId, _) = McpTestTools.Store.Create("Stages");
        var applied = McpTestTools.Dsl.ApplyDsl(sessionId, Dsl);
        await Assert.That(applied.Success).IsTrue().Because(applied.Message);
        var id = NewOrder(sessionId);
        await Assert.That(McpTestTools.Runtime.InvokeAction(sessionId, id, "Activate").Success).IsTrue();

        var result = Result(McpTestTools.Runtime.InvokeAction(sessionId, id, "Cancel"));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(string.Join(",", result.FailedGuards)).IsEqualTo("not_Ready");
    }

    private const string SiblingGuardDsl = """
        domain Siblings
        Order: entity {
          Memo: Text
          New: stage {
            Activate: action { transition to Active }
          }
          Held: stage {
            Cancel: action -> Receipt require Ready { create Receipt { Note: "n" } }
          }
          Active: stage {
            Cancel: action require not Ready { transition to Closed }
          }
          Closed: stage { }
          Ready: policy { Memo is "m" }
        }
        Receipt: entity { Note: Text }
        """;

    [Test]
    public async Task InvokeAction_StageWithoutAction_DoesNotBorrowGuardsFromSiblingStages() {
        var (sessionId, _) = McpTestTools.Store.Create("Siblings");
        var applied = McpTestTools.Dsl.ApplyDsl(sessionId, SiblingGuardDsl);
        await Assert.That(applied.Success).IsTrue().Because(applied.Message);
        var id = NewOrder(sessionId);

        var response = McpTestTools.Runtime.InvokeAction(sessionId, id, "Cancel");

        await Assert.That(response.Success).IsFalse();
        // The module rejects the call ("not valid for the current stage"); nothing about
        // the result may be borrowed from a sibling stage's Cancel (its guards or its Receipt).
        var result = Result(response);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailedGuards.Count).IsEqualTo(0);
        await Assert.That(ReturnShape(response).ReturnTypeName).IsNull();
    }

    private const string ReturnDsl = """
        domain Tickets
        Ticket: entity {
          Memo: Text
          Draft: stage {
            Cancel: action -> Receipt { create Receipt { Note: "done" } }
            Activate: action { transition to Active }
          }
          Active: stage { Cancel: action { transition to Closed } }
          Closed: stage { }
        }
        Receipt: entity { Note: Text }
        """;

    private static (string? ReturnTypeName, string? NewStage) ReturnShape(DomainToolResponse response) {
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(response.Data));
        var r = doc.RootElement.GetProperty("invokeActionResult");
        string? Str(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
            ? v.GetString() : null;
        return (Str("returnTypeName"), Str("newStage"));
    }

    [Test]
    public async Task InvokeAction_SameNamedStageActions_ReportReturnTypeAndNewStagePerStage() {
        var (sessionId, _) = McpTestTools.Store.Create("Tickets");
        var applied = McpTestTools.Dsl.ApplyDsl(sessionId, ReturnDsl);
        await Assert.That(applied.Success).IsTrue().Because(applied.Message);

        var draft = NewTicket(sessionId);
        var fromDraft = ReturnShape(McpTestTools.Runtime.InvokeAction(sessionId, draft, "Cancel"));
        await Assert.That(fromDraft.ReturnTypeName).IsEqualTo("Receipt");

        var active = NewTicket(sessionId);
        await Assert.That(McpTestTools.Runtime.InvokeAction(sessionId, active, "Activate").Success).IsTrue();
        var fromActive = ReturnShape(McpTestTools.Runtime.InvokeAction(sessionId, active, "Cancel"));
        await Assert.That(fromActive.ReturnTypeName).IsNull();
        await Assert.That(fromActive.NewStage).IsEqualTo("Closed");
    }

    private static string NewTicket(string sessionId) {
        var create = McpTestTools.Runtime.CreateInstance(sessionId, "Ticket", """{"Memo":"m"}""");
        if (!create.Success)
            throw new InvalidOperationException(create.Message);
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(create.Data));
        return doc.RootElement.GetProperty("instance").GetProperty("instanceId").GetString()!;
    }

    [Test]
    public async Task InvokeAction_FromDraftStage_UnguardedCancelTransitions() {
        var (sessionId, _) = McpTestTools.Store.Create("Stages");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, Dsl).Success).IsTrue();
        var id = NewOrder(sessionId);

        var result = Result(McpTestTools.Runtime.InvokeAction(sessionId, id, "Cancel"));

        await Assert.That(result.Succeeded).IsTrue().Because(result.ErrorMessage ?? "");
        await Assert.That(result.NewStage).IsEqualTo("Closed");
    }
}