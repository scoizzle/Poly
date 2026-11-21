namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #798: an entry block admits <c>invoke</c>, <c>create</c>, <c>create in</c> and
/// <c>for … invoke</c>, which are action-only in an exit block (DMEFF018). That asymmetry is
/// deliberate: the entry effects run where the instance is created or lands, so a callee or a
/// create cannot double-assign <c>CurrentStage</c> behind an exit block's inlined hop. These rows
/// pin the accepted side: the printed C# agrees with the simulator, so the effects stay legal.
/// </summary>
public class EntryBlockEffectParityTests {
    private const string Items = """
        Child: entity {
          Count: Number default(0)
          Touch: action { assign Count to Count + 1 }
        }
        """;

    // An initial-stage entry `create in` runs in Create and links the new instance on both hosts.
    [Test]
    public async Task EntryCreateIn_GeneratedCSharp_AgreesWithTheSimulator() {
        var scenario = ParityScenario.FromDsl("""
            domain P1
            Parent: entity {
              Kids: many Child
              Start: stage { entry { create in Kids { Count: 1 } } }
            }
            """ + "\n" + Items, "EntryCreateInParity", recordGraph: true);

        var (simulate, printed) = scenario.Run(side => side.Create("Parent"));

        await Assert.That(ParityScenario.Differences(simulate, printed)).IsEmpty();
        await Assert.That(simulate[0].Success).IsTrue();
        await Assert.That(simulate[0].GraphParity).Contains("_kids=[#1]");
        await Assert.That(simulate[0].GraphParity).Contains("#1 {Count=1}");
        await Assert.That(printed[0].GraphParity).Contains("_kids=[#1]");
        await Assert.That(printed[0].GraphParity).Contains("#1 {Count=1}");
    }

    // A landing stage's entry `for … invoke` runs the fan-out on both hosts, so the child is touched once.
    [Test]
    public async Task EntryForInvoke_GeneratedCSharp_AgreesWithTheSimulator() {
        var scenario = ParityScenario.FromDsl("""
            domain P2
            Parent: entity {
              Kids: many Child
              Seed: action { create in Kids { } }
              Go: action { transition to Running }
              Idle: stage { }
              Running: stage { entry { for Kids as k invoke k.Touch } }
            }
            """ + "\n" + Items, "EntryForInvokeParity", recordGraph: true);

        var (simulate, printed) = scenario.Run(side => {
            side.Create("Parent");
            side.Invoke("Seed");
            side.Invoke("Go");
        });

        await Assert.That(ParityScenario.Differences(simulate, printed)).IsEmpty();
        await Assert.That(simulate[2].Success).IsTrue();
        await Assert.That(simulate[2].GraphParity).Contains("#1 {Count=1}");
        await Assert.That(printed[2].GraphParity).Contains("#1 {Count=1}");
    }
}