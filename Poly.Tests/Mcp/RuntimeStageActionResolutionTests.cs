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