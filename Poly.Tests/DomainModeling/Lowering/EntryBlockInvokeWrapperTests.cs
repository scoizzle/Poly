using Poly.Runtime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-04a (issue #825, rulings Q1 and Q3): an <c>invoke</c> in an INITIAL stage's entry block runs in <c>Create</c>, which
/// RETURNS the invoked action's failure on both hosts. Before, the invoke lowered into the void constructor: the simulator
/// dropped the failure (and with the action scope wrapper, the nested rollback made the creating action throw), and the
/// generated C# did not compile (CS0127). The failure path is the callee's own, prefixed by the creating action only:
/// <c>Holder.Make.Kid.Fails</c> from the holder's <c>create in</c>, <c>Kid.Fails</c> from a bare <c>Create</c> (the entry
/// adds no segment: L-03h's option B).
/// </summary>
public class EntryBlockInvokeWrapperTests {
    private static string Dsl(string invokedAction) => $$"""
        domain P
        Holder: entity {
          Note: Text default("")
          kids: many Kid
          Make: action { create in kids { } }
          Touch: action { assign Note to "touched" }
        }
        Kid: entity {
          Count: Number default(0)
          Never: policy { Count > 100 }
          Fails: action require Never { assign Count to Count + 1 }
          Works: action { assign Count to Count + 1 }
          Start: stage { entry { invoke {{invokedAction}} } }
        }
        """;

    [Test]
    public async Task EntryBlockInvoke_WhenTheInvokedActionFails_MakeReturnsTheFailureAndRollsBack() {
        var (domain, _) = EvolvedDomain.FromDsl(Dsl("Fails"));
        var exec = ModuleExec.For(domain);
        var holder = exec.Create("Holder", new Dictionary<string, object?>()).Instance!;

        var make = exec.Invoke(holder, "Make");

        await Assert.That(make.Succeeded).IsFalse();
        await Assert.That(make.Failure!.Guard).IsEqualTo("Never");
        await Assert.That(make.Failure.ElementPath).IsEqualTo("Holder.Make.Kid.Fails");
        await Assert.That(ModuleReads.Related(exec, "kids", holder).Count).IsEqualTo(0);
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
        // The executor is not left broken: an ordinary action on the same instance still commits.
        await Assert.That(exec.Invoke(holder, "Touch").Succeeded).IsTrue();
        await Assert.That(holder.Bag["Note"]).IsEqualTo("touched");
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
    }

    // #825's simulator half: a bare Create used to succeed with the failure dropped.
    [Test]
    public async Task EntryBlockInvoke_WhenTheInvokedActionFails_BareCreateReturnsTheFailure() {
        var (domain, _) = EvolvedDomain.FromDsl(Dsl("Fails"));
        var exec = ModuleExec.For(domain);

        var created = exec.Create("Kid", new Dictionary<string, object?>());

        await Assert.That(created.Succeeded).IsFalse();
        await Assert.That(created.Failure!.Guard).IsEqualTo("Never");
        await Assert.That(created.Failure.ElementPath).IsEqualTo("Kid.Fails");
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
    }

    // #825's generated C# half: it compiles with warnings as errors (it was CS0127) and agrees with the simulator on
    // success, kind, path, guard and the kid count, for the action and for the bare Create.
    [Test]
    public async Task EntryBlockInvoke_GeneratedCSharp_CompilesAndAgreesWithTheSimulator() {
        var scenario = ParityScenario.FromDsl(Dsl("Fails"), "EntryBlockInvokeWrapper", recordGraph: true);

        var (simulate, printed) = scenario.Run(side => {
            side.Create("Holder");
            side.Invoke("Make");
            side.Create("Kid");
        });

        await Assert.That(ParityScenario.Differences(simulate, printed)).IsEmpty();
        await Assert.That(simulate[1].FailurePath).IsEqualTo("Holder.Make.Kid.Fails");
        await Assert.That(printed[1].FailurePath).IsEqualTo("Holder.Make.Kid.Fails");
        await Assert.That(simulate[1].State["kids"]).IsEqualTo("0");
        await Assert.That(printed[1].State["kids"]).IsEqualTo("0");
        await Assert.That(simulate[2].FailurePath).IsEqualTo("Kid.Fails");
        await Assert.That(printed[2].FailurePath).IsEqualTo("Kid.Fails");
        foreach (var outcome in new[] { simulate[1], printed[1], simulate[2], printed[2] }) {
            await Assert.That(outcome.Success).IsFalse();
            await Assert.That(outcome.FailureKind).IsEqualTo("Policy");
            await Assert.That(outcome.FailureGuard).IsEqualTo("Never");
            await Assert.That(outcome.ExceptionType).IsNull();
        }
    }

    /// <summary>The control: the same entry invoke of an action that succeeds creates the kid normally, so the pin above
    /// cannot pass because entry invoke is broken altogether.</summary>
    [Test]
    public async Task EntryBlockInvoke_WhenTheInvokedActionSucceeds_CreatesTheKid() {
        var (domain, _) = EvolvedDomain.FromDsl(Dsl("Works"));
        var exec = ModuleExec.For(domain);
        var holder = exec.Create("Holder", new Dictionary<string, object?>()).Instance!;

        var make = exec.Invoke(holder, "Make");

        await Assert.That(make.Succeeded).IsTrue();
        var kid = ModuleReads.Related(exec, "kids", holder).Single();
        await Assert.That(kid.Bag["Count"]).IsEqualTo(1L);
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
    }
}