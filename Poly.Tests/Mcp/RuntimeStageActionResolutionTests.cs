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
        var create = McpTestTools.Surface.Create(sessionId, "Order", """{"Memo":"m"}""");
        if (!create.Ok)
            throw new InvalidOperationException(create.Summary);
        return create.InstanceId;
    }

    [Test]
    public async Task InvokeAction_FromActiveStage_MapsGuardsOfActiveCancelNotDraftCancel() {
        var sessionId = McpTestTools.Surface.Open("Stages");
        var applied = McpTestTools.Surface.Define(sessionId, Dsl);
        await Assert.That(applied.Success).IsTrue().Because(applied.Summary);
        var id = NewOrder(sessionId);
        await Assert.That(McpTestTools.Surface.Invoke(sessionId, id, "Activate").Ok).IsTrue();

        var result = McpTestTools.Surface.Invoke(sessionId, id, "Cancel").Result;

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
        var sessionId = McpTestTools.Surface.Open("Siblings");
        var applied = McpTestTools.Surface.Define(sessionId, SiblingGuardDsl);
        await Assert.That(applied.Success).IsTrue().Because(applied.Summary);
        var id = NewOrder(sessionId);

        var response = McpTestTools.Surface.Invoke(sessionId, id, "Cancel");

        await Assert.That(response.Ok).IsFalse();
        // The module rejects the call ("not valid for the current stage"); nothing about
        // the result may be borrowed from a sibling stage's Cancel (its guards or its Receipt).
        await Assert.That(response.Data?["invokeActionResult"]).IsNotNull();
        var result = response.Result;
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailedGuards.Count).IsEqualTo(0);
        await Assert.That(result.ReturnTypeName).IsNull();
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

    [Test]
    public async Task InvokeAction_SameNamedStageActions_ReportReturnTypeAndNewStagePerStage() {
        var sessionId = McpTestTools.Surface.Open("Tickets");
        var applied = McpTestTools.Surface.Define(sessionId, ReturnDsl);
        await Assert.That(applied.Success).IsTrue().Because(applied.Summary);

        var draft = NewTicket(sessionId);
        var fromDraft = McpTestTools.Surface.Invoke(sessionId, draft, "Cancel").Result;
        await Assert.That(fromDraft.ReturnTypeName).IsEqualTo("Receipt");

        var active = NewTicket(sessionId);
        await Assert.That(McpTestTools.Surface.Invoke(sessionId, active, "Activate").Ok).IsTrue();
        var fromActive = McpTestTools.Surface.Invoke(sessionId, active, "Cancel").Result;
        await Assert.That(fromActive.ReturnTypeName).IsNull();
        await Assert.That(fromActive.NewStage).IsEqualTo("Closed");
    }

    private static string NewTicket(string sessionId) {
        var create = McpTestTools.Surface.Create(sessionId, "Ticket", """{"Memo":"m"}""");
        if (!create.Ok)
            throw new InvalidOperationException(create.Summary);
        return create.InstanceId;
    }

    [Test]
    public async Task InvokeAction_FromDraftStage_UnguardedCancelTransitions() {
        var sessionId = McpTestTools.Surface.Open("Stages");
        await Assert.That(McpTestTools.Surface.Define(sessionId, Dsl).Success).IsTrue();
        var id = NewOrder(sessionId);

        var result = McpTestTools.Surface.Invoke(sessionId, id, "Cancel").Result;

        await Assert.That(result.Succeeded).IsTrue().Because(result.ErrorMessage ?? "");
        await Assert.That(result.NewStage).IsEqualTo("Closed");
    }
}