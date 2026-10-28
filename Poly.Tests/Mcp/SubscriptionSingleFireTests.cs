using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// F27: one stage transition of a create-in child fires each subscription
/// handler exactly once. A second registration through the kept Create/Attach
/// body double-counted badges, fines, and notes.
/// </summary>
public class SubscriptionSingleFireTests {
    private const string Queue = """
        domain QueueProbe
        uses temporal

        Queue: entity {
          Name: Text
          FinishedCount: Number default(0)
          FailedCount: Number default(0)
          jobs: many Job

          when jobs Done, Failed as job {
            assign FinishedCount to FinishedCount + 1
          }
          when jobs Failed {
            assign FailedCount to FailedCount + 1
          }

          Enqueue: action (title: Text) {
            create in jobs { Title: title }
          }
        }

        Job: entity {
          Title: Text
          Retries: Number default(0)
          MaxRetries: Number default(3)

          CanRetry: policy { Retries < MaxRetries }

          Queued: stage {
            Start: action { transition to Running }
          }
          Running: stage {
            Complete: action { transition to Done }
            Fail: action {
              assign Retries to Retries + 1
              transition to Failed
            }
          }
          Failed: stage {
            Retry: action
              require CanRetry
            {
              transition to Running
            }
          }
          Done: stage { }
        }
        """;

    private static string CreateAndId(string sessionId, string entity, string? props = null) {
        var create = McpTestTools.Runtime.CreateInstance(sessionId, entity, props);
        if (!create.Success)
            throw new InvalidOperationException($"create_instance {entity}: {create.Message}");
        var json = System.Text.Json.JsonSerializer.Serialize(create.Data);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("instance", out var instance)
            && instance.TryGetProperty("instanceId", out var id))
            return id.GetString() ?? throw new InvalidOperationException("missing instanceId");
        throw new InvalidOperationException("missing instance payload");
    }

    private static long Counter(string sessionId, string instanceId, string property) {
        McpTestTools.Store.TryGet(sessionId, out var state);
        return state!.InstanceMap[instanceId].GetProperty<long>(property);
    }

    private static bool Invoke(string sessionId, string instanceId, string action, string? args = null) {
        var result = McpTestTools.Runtime.InvokeAction(sessionId, instanceId, action, args);
        if (!result.Success)
            throw new InvalidOperationException($"invoke_action {action}: {result.Message}");
        return true;
    }

    [Test]
    public async Task OneFailTransition_FiresEachHandlerOnce() {
        var (sessionId, _) = McpTestTools.Store.Create("SingleFire");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, Queue).Success).IsTrue();

        var queueId = CreateAndId(sessionId, "Queue", """{"Name":"Q"}""");
        Invoke(sessionId, queueId, "Enqueue", """{"title":"build"}""");
        McpTestTools.Store.TryGet(sessionId, out var state);
        var jobId = state!.InstanceMap
            .First(pair => pair.Value.Entity.Name == "Job").Key;

        Invoke(sessionId, jobId, "Start");
        Invoke(sessionId, jobId, "Fail");

        await Assert.That(Counter(sessionId, queueId, "FinishedCount")).IsEqualTo(1);
        await Assert.That(Counter(sessionId, queueId, "FailedCount")).IsEqualTo(1);
    }

    [Test]
    public async Task ThreeFailRetries_CountersStayPerTransition() {
        var (sessionId, _) = McpTestTools.Store.Create("SingleFireMulti");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, Queue).Success).IsTrue();

        var queueId = CreateAndId(sessionId, "Queue", """{"Name":"Q"}""");
        Invoke(sessionId, queueId, "Enqueue", """{"title":"build"}""");
        McpTestTools.Store.TryGet(sessionId, out var state);
        var jobId = state!.InstanceMap
            .First(pair => pair.Value.Entity.Name == "Job").Key;

        Invoke(sessionId, jobId, "Start");
        for (var i = 0; i < 3; i++) {
            Invoke(sessionId, jobId, "Fail");
            if (i < 2)
                Invoke(sessionId, jobId, "Retry");
        }

        await Assert.That(Counter(sessionId, queueId, "FinishedCount")).IsEqualTo(3);
        await Assert.That(Counter(sessionId, queueId, "FailedCount")).IsEqualTo(3);
    }
}