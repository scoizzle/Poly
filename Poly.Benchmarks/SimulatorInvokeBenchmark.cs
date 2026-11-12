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
/// The simulator-invoke baseline: one module action call through the interpreter where the action
/// assigns a <c>unique</c> property, so every call runs the storage port's unique check. No golden
/// domain has such an action, which is why the document is the inline <c>Parking</c> below; the
/// golden-domain action costs live in <c>invoke.csv</c>. The committed copy is
/// <c>Poly.Benchmarks/baselines/simulator.csv</c>, produced by:
/// <code>
/// dotnet run -c Release --project Poly.Benchmarks -- --job short --filter '*SimulatorInvokeBenchmark*'
/// </code>
/// with BenchmarkDotNet 0.15.8, then copied verbatim from the exporter's report.
/// <para>
/// The case is built in the argument source, not a <c>[GlobalSetup]</c>, so <c>--list flat</c>
/// enumerates it and a broken document fails before any measurement. The plate argument is a
/// per-invocation counter, so the key names the unique-port shape rather than a value.
/// </para>
/// <para>
/// <b>Read the row as a rebinding cost, not an invoke cost.</b> <c>Relabel</c> takes a parameter, so
/// every invoke goes <c>FindMethod</c> → <c>BindOne</c>, which builds a fresh method body; the
/// compiled cache's <c>ReferenceEquals</c> check can never hit, and <c>Interpreter.Analyze</c> plus
/// <c>Interpreter.Compile</c> re-run per call. That is the 1.77 MB in this row. The storage-port
/// unique check is still exercised, but its cost is not separable from the recompilation until that
/// cache miss is fixed, so a Q1 after-number on this row is not attributable either.
/// </para>
/// <para>
/// <b>Usage rule.</b> Compare <c>Allocated</c> against the committed file — it is stable for a
/// given runtime — and re-run <c>Mean</c> side by side on one machine. The noise caveat covers every
/// row: <c>--job short</c> <c>Mean</c> is noisy, so this row is not a threshold.
/// </para>
/// <para>
/// <b>After-number procedure.</b> Re-run the command above with the same <c>--job short</c> on the
/// same machine, copy the report over this file, then diff the two CSVs by <c>Allocated</c> per row
/// and re-run <c>Mean</c> for both revisions side by side.
/// </para>
/// <para>
/// <b>Base.</b> This file is a before-number taken at <c>0b40fc16</c> (the S-11 merge), on the
/// branch's base <c>origin/master</c>. It is a record, not a target.
/// </para>
/// </summary>
[Config(typeof(PolyBenchmarkConfig))]
[MemoryDiagnoser]
public class SimulatorInvokeBenchmark {
    /// <summary>The benchmark method name, pinned by the smoke test's row check.</summary>
    public const string Method = "UniqueAssignAction";

    /// <summary>The one case's key: a unique-property assign through the storage port.</summary>
    public const string Key = "parking/permit/relabel(unique)";

    /// <summary>The single case, built once per enumeration by the argument source.</summary>
    public static IEnumerable<UniqueAssignCase> Cases() => [UniqueAssignCase.Create()];

    /// <summary>Relabels one permit; the <c>unique</c> assign runs the storage port's check.</summary>
    [Benchmark]
    [ArgumentsSource(nameof(Cases))]
    public bool UniqueAssignAction(UniqueAssignCase entry) => entry.Invoke();
}

/// <summary>
/// The simulator's one case: a <c>Parking.Permit</c> instance whose <c>Relabel</c> assigns a
/// <c>unique</c> property, so each invoke runs the storage port. <see cref="ToString"/> is the CSV
/// parameter cell and the baseline's key.
/// </summary>
public sealed class UniqueAssignCase {
    private readonly ModuleInstanceExecutor _executor;
    private readonly ModuleInstance _permit;
    private int _next;

    private UniqueAssignCase(ModuleInstanceExecutor executor, ModuleInstance permit) {
        _executor = executor;
        _permit = permit;
    }

    /// <summary>Loads the inline document, lowers it and registers one permit.</summary>
    public static UniqueAssignCase Create() {
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
        var executor = new ModuleInstanceExecutor(domain);
        var created = executor.Create("Permit", new Dictionary<string, object?> { ["Plate"] = "P0" });
        if (!created.Succeeded || created.Instance is not { } permit)
            throw new InvalidOperationException(created.Error ?? "Create failed.");
        return new UniqueAssignCase(executor, permit);
    }

    /// <summary>Invokes <c>Relabel</c> with a fresh plate, so the unique check never short-circuits.</summary>
    public bool Invoke() =>
        _executor.Invoke(_permit, "Relabel",
            new Dictionary<string, object?> { ["plate"] = $"P{++_next}" }).Succeeded;

    public override string ToString() => SimulatorInvokeBenchmark.Key;
}