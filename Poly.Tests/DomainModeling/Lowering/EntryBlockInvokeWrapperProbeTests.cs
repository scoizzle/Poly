using Poly.Runtime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-03b probe (issue 825): an invoke in an INITIAL stage's entry block lowers into the void constructor, which drops the
/// failed result. On master the create-in action succeeds (simulator); with the wrapper the failing invoke's nested scope
/// rolls the whole frame back and the outer action's commit throws. RED on the wrapper by design; never merges.
/// </summary>
public class EntryBlockInvokeWrapperProbeTests {
    private const string Dsl = """
        domain P
        Holder: entity {
          kids: many Kid
          Make: action { create in kids { } }
        }
        Kid: entity {
          Count: Number default(0)
          Never: policy { Count > 100 }
          Z: action require Never { assign Count to Count + 1 }
          Start: stage { entry { invoke Z } }
        }
        """;

    [Test]
    public async Task CreateInWhoseInitialEntryInvokeFails_StillSucceeds_AsOnMaster() {
        var (domain, _) = EvolvedDomain.FromDsl(Dsl);
        var exec = ModuleExec.For(domain);
        var holder = exec.Create("Holder", new Dictionary<string, object?>()).Instance!;

        var make = exec.Invoke(holder, "Make");

        await Assert.That(make.Succeeded).IsTrue();
        await Assert.That(ModuleReads.Related(exec, "kids", holder).Count).IsEqualTo(1);
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
    }
}