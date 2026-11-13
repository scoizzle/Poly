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
          queue: Queue via jobs

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
        var create = McpTestTools.Surface.Create(sessionId, entity, props);
        if (!create.Ok)
            throw new InvalidOperationException($"create_instance {entity}: {create.Summary}");
        return create.InstanceId;
    }

    private static long Counter(string sessionId, string instanceId, string property) {
        McpTestTools.Store.TryGet(sessionId, out var state);
        return (long)state!.InstanceMap[instanceId][property]!;
    }

    private static bool Invoke(string sessionId, string instanceId, string action, string? args = null) {
        var result = McpTestTools.Surface.Invoke(sessionId, instanceId, action, args);
        if (!result.Ok)
            throw new InvalidOperationException($"invoke_action {action}: {result.Summary}");
        return true;
    }

    [Test]
    public async Task OneFailTransition_FiresEachHandlerOnce() {
        var sessionId = McpTestTools.Surface.Open("SingleFire");
        await Assert.That(McpTestTools.Surface.Define(sessionId, Queue).Success).IsTrue();

        var queueId = CreateAndId(sessionId, "Queue", """{"Name":"Q"}""");
        Invoke(sessionId, queueId, "Enqueue", """{"title":"build"}""");
        McpTestTools.Store.TryGet(sessionId, out var state);
        var jobId = state!.Executor.Instances
            .First(i => i.EntityName == "Job").Id;

        Invoke(sessionId, jobId, "Start");
        Invoke(sessionId, jobId, "Fail");

        await Assert.That(Counter(sessionId, queueId, "FinishedCount")).IsEqualTo(1);
        await Assert.That(Counter(sessionId, queueId, "FailedCount")).IsEqualTo(1);
    }

    [Test]
    public async Task ThreeFailRetries_CountersStayPerTransition() {
        var sessionId = McpTestTools.Surface.Open("SingleFireMulti");
        await Assert.That(McpTestTools.Surface.Define(sessionId, Queue).Success).IsTrue();

        var queueId = CreateAndId(sessionId, "Queue", """{"Name":"Q"}""");
        Invoke(sessionId, queueId, "Enqueue", """{"title":"build"}""");
        McpTestTools.Store.TryGet(sessionId, out var state);
        var jobId = state!.Executor.Instances
            .First(i => i.EntityName == "Job").Id;

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