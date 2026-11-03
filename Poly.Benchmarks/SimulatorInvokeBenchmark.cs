using BenchmarkDotNet.Attributes;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

namespace Poly.Benchmarks;

/// <summary>
/// One module action call through the interpreter: <c>Relabel(plate)</c> assigns a unique
/// property, so each call runs the storage-port unique check.
/// </summary>
[MemoryDiagnoser]
public class SimulatorInvokeBenchmark {
    private ModuleInstanceExecutor _executor = null!;
    private ModuleInstance _permit = null!;
    private int _next;

    [GlobalSetup]
    public void Setup() {
        var session = DomainSession.ForExtensions(["persistence"]);
        var changes = new PolyDslParser("""
            domain Parking
            Permit: entity {
              Plate: Text unique
              Relabel: action (plate: Text) { assign Plate to plate }
            }
            """, session).Parse();
        var result = new DomainEvolution(new Domain("_", [])).ApplyAndAnalyze(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary);
        var domain = result.Root;
        RuntimeAnalysisCache.GetOrLower(domain, session, result.Analysis);
        _executor = new ModuleInstanceExecutor(domain);
        var created = _executor.Create("Permit", new Dictionary<string, object?> { ["Plate"] = "P0" });
        if (!created.Succeeded || created.Instance is not { } permit)
            throw new InvalidOperationException(created.Error ?? "Create failed.");
        _permit = permit;
    }

    [Benchmark]
    public bool UniqueAssignAction() =>
        _executor.Invoke(_permit, "Relabel",
            new Dictionary<string, object?> { ["plate"] = $"P{++_next}" }).Succeeded;
}