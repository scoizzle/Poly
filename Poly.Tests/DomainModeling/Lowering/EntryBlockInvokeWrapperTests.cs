using Poly.Runtime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-03b known corner (issue #825, owner ruling 2026-11-13): an <c>invoke</c> in an INITIAL stage's entry block lowers
/// into the void constructor, which drops the invoked action's failed result. On master the simulator silently dropped the
/// failure and the create succeeded; with the action scope wrapper the failing invoke's nested scope rolls the whole frame
/// back, so the creating action throws and creates nothing. Rulings Q1 and Q3 want <c>Create</c> to RETURN the failure; row
/// L-04 moves entry inlining into <c>Create</c> and then these tests are replaced by L-04's red test on purpose.
/// Nothing is asserted about the generated C#: it does not compile for this DSL on master either (#825 records that).
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
    public async Task EntryBlockInvoke_WhenTheInvokedActionFails_ThrowsRolledBack_UntilL04ReturnsTheFailure() {
        var (domain, _) = EvolvedDomain.FromDsl(Dsl("Fails"));
        var exec = ModuleExec.For(domain);
        var holder = exec.Create("Holder", new Dictionary<string, object?>()).Instance!;

        var thrown = Assert.Throws<InvalidOperationException>(() => exec.Invoke(holder, "Make"));

        await Assert.That(thrown.Message).Contains("rolled back");
        await Assert.That(ModuleReads.Related(exec, "kids", holder).Count).IsEqualTo(0);
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
        // The executor is not left broken: an ordinary action on the same instance still commits.
        await Assert.That(exec.Invoke(holder, "Touch").Succeeded).IsTrue();
        await Assert.That(holder.Bag["Note"]).IsEqualTo("touched");
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
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