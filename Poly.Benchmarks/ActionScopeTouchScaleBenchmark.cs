using BenchmarkDotNet.Attributes;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

namespace Poly.Benchmarks;

/// <summary>
/// L-04a (L-03c's review): the per-action cost of <c>ActionScope.Touch</c> grows with the collections of the entity it
/// touches, because the snapshot copies them. One owner with <c>N</c> children, one cached successful action
/// (<c>Tick</c>, an assign) on the owner: the allocation per invoke is the number to read as <c>N</c> grows. It runs on
/// the simulator (the module wrapper's bag snapshot, on top of the executor's capture until X-04 removes that); the
/// generated C# copies the same lists in <c>__Snapshot</c>. Run:
/// <code>dotnet run -c Release --project Poly.Benchmarks -- --job short --filter '*ActionScopeTouchScaleBenchmark*'</code>
/// </summary>
[Config(typeof(PolyBenchmarkConfig))]
[MemoryDiagnoser]
public class ActionScopeTouchScaleBenchmark {
    private const string Dsl = """
        domain Scale
        Owner: entity {
          Count: Number default(0)
          kids: many Kid
          Tick: action { assign Count to Count + 1 }
        }
        Kid: entity { N: Number default(0) }
        """;

    private ModuleInstanceExecutor _executor = null!;
    private ModuleInstance _owner = null!;

    [Params(1, 100, 10000)]
    public int Children { get; set; }

    [GlobalSetup]
    public void Setup() {
        var session = BenchmarkSupport.AuthoringSession(Dsl, "ActionScopeTouchScaleBenchmark");
        var changes = new PolyDslParser(Dsl, session).Parse();
        var result = new DomainEvolution(new Domain("_", [])).ApplyAndAnalyze(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Scale domain does not analyze: {result.FailureSummary}");
        RuntimeAnalysisCache.GetOrLower(result.Root, session, result.Analysis);
        _executor = new ModuleInstanceExecutor(result.Root);
        _owner = _executor.Create("Owner", new Dictionary<string, object?>()).Instance!;
        for (var i = 0; i < Children; i++) {
            var kid = _executor.Create("Kid", new Dictionary<string, object?>()).Instance!;
            _executor.Link("kids", _owner, kid);
        }
    }

    [Benchmark]
    public bool Tick() => _executor.Invoke(_owner, "Tick", null).Succeeded;
}