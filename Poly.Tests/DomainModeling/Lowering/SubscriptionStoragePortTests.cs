namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #830: a subscription handler that needs the storage port lowers, and both hosts run it. The port reaches the
/// handler through the dispatcher's registry loop, whose element is a loop variable: the contributed-parameter pass
/// resolved only <c>this</c> and type-name receivers, so it could not thread the port into <c>sub.Handler(…)</c>.
/// </summary>
public class SubscriptionStoragePortTests {
    // The issue's reproduction: the handler assigns a `unique` property, so the storage library contributes an
    // IStoragePort parameter to the handler and every caller up to the host.
    private const string UniqueAssignInHandler = """
        domain P
        uses persistence
        Owner: entity {
          Code: Text unique default("c")
          items: many Item
          Add: action (n: Text) { create in items { Name: n } }
          when items Done { assign Code to "x" }
        }
        Item: entity {
          Name: Text
          Open: stage { Finish: action { transition to Done } }
          Done: stage { }
        }
        """;

    [Test]
    public async Task SubscriptionHandler_ThatAssignsAUniqueProperty_RunsOnBothHosts() {
        object? owner = null;
        var outcomes = await ParityScenario.FromDsl(UniqueAssignInHandler, "SubscriptionStoragePort")
            .AssertAgree(side => {
                owner = side.Create("Owner");
                side.Invoke("Add", ("n", "i"));
                side.UseLinked("items");
                side.Invoke("Finish");
                side.Use(owner);
            });

        await Assert.That(outcomes[3].Success).IsTrue().Because(outcomes[3].Message ?? "");
        await Assert.That(outcomes[4].State["Code"]).IsEqualTo("x");
    }

    // The same obligation one call deeper: the handler invokes an action that assigns the unique property, so the
    // port is threaded through the handler, the dispatcher's loop and the invoker.
    private const string UniqueAssignBehindInvoke = """
        domain Q
        uses persistence
        Owner: entity {
          Code: Text unique default("c")
          items: many Item
          Bump: action { assign Code to "y" }
          Add: action (n: Text) { create in items { Name: n } }
          when items Done { invoke Bump }
        }
        Item: entity {
          Name: Text
          Open: stage { Finish: action { transition to Done } }
          Done: stage { }
        }
        """;

    [Test]
    public async Task SubscriptionHandler_ThatInvokesAnActionAssigningAUniqueProperty_RunsOnBothHosts() {
        object? owner = null;
        var outcomes = await ParityScenario.FromDsl(UniqueAssignBehindInvoke, "SubscriptionStoragePortInvoke")
            .AssertAgree(side => {
                owner = side.Create("Owner");
                side.Invoke("Add", ("n", "i"));
                side.UseLinked("items");
                side.Invoke("Finish");
                side.Use(owner);
            });

        await Assert.That(outcomes[3].Success).IsTrue().Because(outcomes[3].Message ?? "");
        await Assert.That(outcomes[4].State["Code"]).IsEqualTo("y");
    }
}