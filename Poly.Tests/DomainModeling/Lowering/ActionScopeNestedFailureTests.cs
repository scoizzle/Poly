using Poly.Runtime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-03b closing round (N3): a subscription handler (<c>when</c> body) is a void method, so an <c>invoke</c> inside it
/// swallows the callee's failure and the outer action carries on. With the action scope wrapper the failing callee
/// opens a NESTED scope, and its rollback closes the whole root frame, so the outer action's commit throws. On
/// master the outer action succeeds (Count stays 0). This test asserts master's behaviour and is RED on the wrapper.
/// (The printed host cannot compile this DSL on master either: CS0127, a handler returns void; not part of this row.)
/// </summary>
public class ActionScopeNestedFailureTests {
    private const string Dsl = """
        domain P
        Owner: entity {
          Count: Number default(0)
          items: many Item
          Never: policy { Count > 100 }
          Y: action require Never { assign Count to Count + 1 }
          Add: action { create in items { } }
          Run: action { for i in items { invoke i.Finish } }
          when items Done { invoke Y }
        }
        Item: entity {
          Open: stage { Finish: action { transition to Done } }
          Done: stage { }
        }
        """;

    [Test]
    public async Task OuterAction_WhenAHandlerInvokesAFailingAction_StillSucceeds_AsOnMaster() {
        var (domain, _) = EvolvedDomain.FromDsl(Dsl);
        var exec = ModuleExec.For(domain);
        var owner = exec.Create("Owner", new Dictionary<string, object?>()).Instance!;
        await Assert.That(exec.Invoke(owner, "Add").Succeeded).IsTrue();

        var run = exec.Invoke(owner, "Run");

        await Assert.That(run.Succeeded).IsTrue();
        await Assert.That(owner.Bag["Count"]).IsEqualTo(0L);
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
    }
}