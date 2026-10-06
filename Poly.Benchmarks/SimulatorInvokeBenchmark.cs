using BenchmarkDotNet.Attributes;

using Poly.DomainModeling;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Runtime;

namespace Poly.Benchmarks;

/// <summary>
/// One simulator action call through the VM: <c>Relabel(plate)</c> assigns a unique
/// property, so each call runs the EnsureUnique host job against the store.
/// </summary>
[MemoryDiagnoser]
public class SimulatorInvokeBenchmark {
    private DomainEntityInstance _permit = null!;
    private int _next;

    [GlobalSetup]
    public void Setup() {
        var changes = new PolyDslParser("""
            domain Parking
            Permit: entity {
              Plate: Text unique
              Relabel: action (plate: Text) { assign Plate to plate }
            }
            """).Parse();
        var result = new DomainEvolution(new Domain("_", [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary);
        var domain = result.Root;
        var permit = domain.Types.OfType<Entity>().Single();
        _permit = DomainEntityInstance.Create(permit,
            new Dictionary<string, object?> { ["Plate"] = "P0" }, domain: domain);
        new DomainInstanceStore().Add(_permit);
    }

    [Benchmark]
    public bool UniqueAssignAction() =>
        _permit.InvokeAction("Relabel",
            new Dictionary<string, object?> { ["plate"] = $"P{++_next}" }).Succeeded;
}
