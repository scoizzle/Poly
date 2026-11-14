using Poly.DslCompiler;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Scaffolding;

// L-15a fix round, fix 4 probe (scaffolding: deleted in the last commit).
public class L15aFix4ProbeTests {
    const string Dsl = """
        domain Probe
        Child: entity {
          Name: Text
        }
        Parent: entity {
          Name: Text
          kid: Child
          AddKid: action { create in kid { Name: "x" } }
        }
        """;

    [Test]
    public async Task Probe_CreateInRequiredSingularNavigation() {
        var analysis = new Poly.DslCompiler.DslCompiler().Compile(Dsl, CompileMode.All);
        Console.WriteLine($"PROBE analyze success={analysis.Success} errors=[{string.Join(" | ", analysis.Errors ?? [])}]");
        var scenario = ParityScenario.FromDsl(Dsl, "Fix4Probe");
        var (sim, printed) = scenario.Run(side => {
            var child = side.Create("Child", ("Name", "c"));
            side.Create("Parent", ("Name", "p"), ("kid", child));
            side.Invoke("AddKid");
        });
        foreach (var (name, outcomes) in new[] { ("simulate", sim), ("printed", printed) })
            foreach (var o in outcomes)
                Console.WriteLine($"PROBE {name} {o.Step}: success={o.Success} kind={o.FailureKind} path={o.FailurePath} message={o.Message} ex={o.ExceptionType}");
        await Assert.That(sim.Count).IsGreaterThan(0);
    }
}