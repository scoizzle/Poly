using System.Globalization;

using BenchmarkDotNet.Attributes;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

namespace Poly.Benchmarks;

/// <summary>Which of the three shapes one action invoke took.</summary>
public enum InvokeShape {
    /// <summary>The action returned a success outcome.</summary>
    Succeeded,

    /// <summary>The action returned a failure outcome — the assign guard's <c>DomainResult.Failure</c> path.</summary>
    ReturnedFailure,

    /// <summary>The action threw <see cref="ConstraintFailureException"/> — the void-context path.</summary>
    Threw,
}

/// <summary>
/// The two-phase action-invoke baseline: one row per golden-domain action call, measuring
/// <c>ModuleInstanceExecutor.Invoke</c> — argument binding, the action body, its assign guards, and
/// the host's <c>Transact</c> capture/restore. The committed copy is
/// <c>Poly.Benchmarks/baselines/invoke.csv</c>, produced by:
/// <code>
/// dotnet run -c Release --project Poly.Benchmarks -- --job short --filter '*TwoPhaseInvokeBenchmark*'
/// </code>
/// with BenchmarkDotNet 0.15.8, then copied verbatim from the exporter's report.
/// <para>
/// <b>Session.</b> The cases are built on <see cref="BenchmarkSupport.AuthoringSession"/>:
/// <c>DomainSession.ForAuthoringSource(text, ExtensionCatalog.ProductAuthoring)</c>, the same
/// authoring pipeline <c>PolyCompiler</c> runs for the define gate (<c>apply_dsl</c> /
/// <c>validate_dsl</c>). It differs only in catalog — <see cref="ExtensionCatalog.Core"/> here,
/// <c>ProductCatalog.Model</c> there — and none of the golden documents lists a vendor
/// <c>uses</c> id, so no vendor library contributes an analyzer or an artifact to these cases.
/// </para>
/// <para>
/// <b>What is pre-Q1.</b> Today's invoke is the host-side <c>Transact</c> pair: <c>Capture</c> the
/// instance state before the body and <c>Restore</c> it when the outcome fails, plus
/// <c>keepConstraintMutations: true</c> on the call. Q1's after-number removes that host rollback and
/// puts the two phases in the module. The refused row is <em>not</em> a thrown path: a refused
/// <c>Load</c> returns <c>ModuleOutcome.Fail</c>, because the action has a <c>DomainResult</c> return
/// type and the assign guard therefore returns <c>Failure</c>; the throw path belongs to void
/// contexts (a ctor or a stage entry). The <see cref="ConstraintFailureException"/> catch below is
/// that throw path's shape, kept so the row measures the same op if a failure ever moves to a throw.
/// </para>
/// <para>
/// <b>Usage rule.</b> Compare <c>Allocated</c> against the committed file — it is stable for a
/// given runtime — and re-run <c>Mean</c> side by side on one machine. The noise caveat covers every
/// row: <c>--job short</c> <c>Mean</c> is noisy, so no row is a threshold and a regression is a
/// side-by-side re-run, never a comparison against this file alone.
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
public class TwoPhaseInvokeBenchmark {
    /// <summary>The benchmark method name, pinned by the smoke test's row check.</summary>
    public const string Method = "TwoPhase_ActionInvoke";

    /// <summary>
    /// One case per action shape: a validated assign, a refused assign, and a transition whose
    /// entry effect assigns. The executor, the domain and every instance are built in the factory,
    /// so <c>--list flat</c> proves each case loads and a failure names the document.
    /// </summary>
    public static IEnumerable<InvokeCase> Cases() => Build();

    /// <summary>Runs one case's action once and reports the shape its outcome took.</summary>
    [Benchmark]
    [ArgumentsSource(nameof(Cases))]
    public InvokeShape TwoPhase_ActionInvoke(InvokeCase entry) => ShapeOf(entry);

    /// <summary>Runs the case once and reports which of the three shapes it took.</summary>
    public static InvokeShape ShapeOf(InvokeCase entry) {
        try {
            return entry.Executor.Invoke(entry.Instance, entry.Action, entry.Arguments).Succeeded
                ? InvokeShape.Succeeded
                : InvokeShape.ReturnedFailure;
        }
        catch (ConstraintFailureException) {
            return InvokeShape.Threw;
        }
    }

    /// <summary>Builds the three cases, or throws naming the document, entity or action that failed.</summary>
    internal static IReadOnlyList<InvokeCase> Build() {
        const string relativePath = "docs/probes/fleet-eval/09-transport/warehouse.poly";
        var text = BenchmarkSupport.ReadDocument(relativePath);
        var session = BenchmarkSupport.AuthoringSession(text, relativePath);

        IReadOnlyList<DomainChange> changes;
        try {
            changes = new PolyDslParser(text, session).Parse();
        }
        catch (Exception exception) {
            throw new InvalidOperationException(
                $"Invoke corpus document '{relativePath}' does not parse: {exception.Message}", exception);
        }

        var result = new DomainEvolution(new Domain("_", [])).ApplyAndAnalyze(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Invoke corpus document '{relativePath}' does not analyze: {result.FailureSummary}");

        var domain = result.Root;
        RuntimeAnalysisCache.GetOrLower(domain, session, result.Analysis);
        var executor = new ModuleInstanceExecutor(domain);

        var depot = Create(executor, "Warehouse", new Dictionary<string, object?> {
            ["Code"] = "WH-001",
            ["Name"] = "Depot",
            ["Zip"] = "12345",
            ["Capacity"] = 100,
        });

        return [
            new InvokeCase("warehouse/truck/load(0)", "Load",
                new Dictionary<string, object?> { ["weight"] = 0 },
                InvokeShape.Succeeded, executor, Truck(executor, depot, "1HGCM82633A004352")),
            new InvokeCase("warehouse/truck/load(40001)", "Load",
                new Dictionary<string, object?> { ["weight"] = 40001 },
                InvokeShape.ReturnedFailure, executor, Truck(executor, depot, "1HGCM82633A004353")),
            new InvokeCase("warehouse/truck/dispatch()", "Dispatch",
                new Dictionary<string, object?>(),
                InvokeShape.Succeeded, executor, Truck(executor, depot, "1HGCM82633A004354")),
        ];
    }

    private static ModuleInstance Truck(
        ModuleInstanceExecutor executor, ModuleInstance depot, string vin) =>
        Create(executor, "Truck", new Dictionary<string, object?> {
            ["Vin"] = vin,
            ["MaxLoad"] = 40000,
            ["warehouse"] = depot.Id,
        });

    private static ModuleInstance Create(
        ModuleInstanceExecutor executor, string entity, IReadOnlyDictionary<string, object?> values) {
        var created = executor.Create(entity, values);
        if (!created.Succeeded || created.Instance is not { } instance)
            throw new InvalidOperationException(
                $"Invoke corpus cannot create '{entity}': {created.Error ?? "no instance returned"}");
        return instance;
    }
}

/// <summary>
/// One invoke case: a registered instance, the action to call, its arguments and the outcome shape
/// the action is expected to take. <see cref="ToString"/> is the CSV parameter cell and the
/// baseline's key — <c>&lt;domain&gt;/&lt;entity&gt;/&lt;action&gt;(&lt;args&gt;)</c>.
/// </summary>
public sealed record InvokeCase(
    string Key,
    string Action,
    IReadOnlyDictionary<string, object?> Arguments,
    InvokeShape Expected,
    ModuleInstanceExecutor Executor,
    ModuleInstance Instance) {
    public override string ToString() => Key;

    /// <summary>
    /// The instance's <c>CurrentLoad</c> read straight from the bag, so a refused invoke can prove
    /// it kept no mutation on any repeat.
    /// </summary>
    public double Load() => Convert.ToDouble(Instance.Bag["CurrentLoad"], CultureInfo.InvariantCulture);
}