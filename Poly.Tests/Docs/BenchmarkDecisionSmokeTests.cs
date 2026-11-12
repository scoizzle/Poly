using Poly.Benchmarks;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Docs;

/// <summary>
/// Guards the two decision baselines — <c>Poly.Benchmarks/baselines/invoke.csv</c> and
/// <c>analysis.csv</c> — plus the simulator's single unique-port row in <c>simulator.csv</c>. Each
/// file is the CSV exporter's report from one <c>--job short</c> run of its benchmark class, copied
/// verbatim.
/// <para>
/// The baselines pin schema and coverage, never a number: <c>Mean</c> is machine-dependent, so
/// <c>Mean &gt; 0</c> is a schema check only, and a regression is judged by re-running the
/// benchmark side by side. <c>Allocated</c> is stable for a given runtime and is the column to
/// compare against these files. The noise caveat covers every row: <c>--job short</c> <c>Mean</c> is
/// noisy on all of them, so no row is a threshold.
/// </para>
/// </summary>
public sealed class BenchmarkDecisionSmokeTests {
    [Test]
    public async Task AnalysisCorpus_TiesTheBenchmarkDocumentListToTheGoldenCatalog() {
        var recorded = AnalysisBenchmark.Documents.Order(StringComparer.Ordinal).ToList();
        var expected = GoldenDomains.Compilable
            .Select(domain => domain.Path)
            .Order(StringComparer.Ordinal)
            .ToList();
        await Assert.That(string.Join(", ", recorded)).IsEqualTo(string.Join(", ", expected))
            .Because("the analysis row must cover exactly GoldenDomains.Compilable, so a moved golden cannot leave the baseline behind");
    }

    [Test]
    public async Task AnalysisBaseline_ExistsAndNamesEveryGoldenDomain() {
        await Assert.That(File.Exists(BenchmarkBaseline.AnalysisCsvPath)).IsTrue()
            .Because($"{BenchmarkBaseline.AnalysisCsvPath} is the committed analysis baseline; the command that produces it is in Poly.Benchmarks/AnalysisBenchmark.cs");

        var rows = BenchmarkBaseline.Read(BenchmarkBaseline.AnalysisCsvPath);
        var recorded = rows.Select(row => row.Entry).Order(StringComparer.Ordinal).ToList();
        var expected = GoldenDomains.Compilable
            .Select(domain => "analysis/" + domain.Name)
            .Order(StringComparer.Ordinal)
            .ToList();
        await Assert.That(string.Join(", ", recorded)).IsEqualTo(string.Join(", ", expected))
            .Because("every analysis row must name a golden domain, and every golden domain must have a row");

        var foreign = rows
            .Where(row => row.Method != AnalysisBenchmark.Method)
            .Select(row => $"{row.Method} {row.Entry}")
            .ToList();
        await Assert.That(foreign).IsEmpty()
            .Because($"{BenchmarkBaseline.AnalysisCsvPath} must hold only {AnalysisBenchmark.Method} rows");
    }

    [Test]
    public async Task DecisionBenchmarks_RunOneIteration() {
        var analysisCases = AnalysisBenchmark.Cases().ToList();
        await Assert.That(analysisCases.Count).IsGreaterThan(0)
            .Because("the analysis argument source must enumerate every golden document");
        var analysis = new AnalysisBenchmark();
        foreach (var entry in analysisCases) {
            var result = analysis.Analysis_DomainGate(entry);
            await Assert.That(result.HasErrors).IsFalse()
                .Because($"{entry} must analyze clean; a row over an error tree measures nothing");
        }

        var simulator = new SimulatorInvokeBenchmark();
        var simulatorCases = SimulatorInvokeBenchmark.Cases().ToList();
        await Assert.That(simulatorCases.Count).IsEqualTo(1)
            .Because("the simulator argument source is the single unique-port case");
        await Assert.That(simulator.UniqueAssignAction(simulatorCases[0])).IsTrue()
            .Because("the simulator's unique-assign case must succeed once");

        var invokeCases = TwoPhaseInvokeBenchmark.Cases().ToList();
        await Assert.That(invokeCases.Count).IsGreaterThan(0)
            .Because("the invoke argument source must enumerate every action case");
        var invoke = new TwoPhaseInvokeBenchmark();
        foreach (var entry in invokeCases) {
            var shape = invoke.TwoPhase_ActionInvoke(entry);
            await Assert.That(shape).IsEqualTo(entry.Expected)
                .Because($"{entry.Key} must take its declared outcome shape");
        }
    }

    /// <summary>
    /// C5: a case whose action or entity the module does not declare throws at factory time, naming
    /// both, instead of measuring a <see cref="InvokeShape.ReturnedFailure"/>.
    /// </summary>
    [Test]
    public async Task InvokeCases_ResolveEveryEntityAndActionAtFactoryTime() {
        var entry = TwoPhaseInvokeBenchmark.Cases()
            .First(candidate => candidate.Action == TwoPhaseInvokeBenchmark.TransitionAction);

        var missingAction = Assert.Throws<InvalidOperationException>(() => TwoPhaseInvokeBenchmark.Case(
            entry.Domain, entry.Executor, entry.Instance,
            "warehouse/truck/nosuch()", "NoSuchAction",
            new Dictionary<string, object?>(), InvokeShape.ReturnedFailure));
        await Assert.That(missingAction.Message).Contains("NoSuchAction")
            .Because("the error must name the action the case asked for");
        await Assert.That(missingAction.Message).Contains(entry.Instance.EntityName)
            .Because("the error must name the entity the case asked for");

        var missingEntity = Assert.Throws<InvalidOperationException>(
            () => TwoPhaseInvokeBenchmark.RequireDeclared(entry.Domain, "NoSuchEntity", "Load"));
        await Assert.That(missingEntity.Message).Contains("NoSuchEntity")
            .Because("the error must name the entity the case asked for");
    }

    /// <summary>
    /// C2: every decision baseline's numeric columns stay in range, so a refresh that wrote a zero or
    /// a negative cannot pass as a measurement. A schema check, never a threshold.
    /// </summary>
    [Test]
    public async Task DecisionBaselines_PinNumericRange() {
        foreach (var path in new[] {
                     BenchmarkBaseline.InvokeCsvPath,
                     BenchmarkBaseline.AnalysisCsvPath,
                     BenchmarkBaseline.SimulatorCsvPath,
                 }) {
            var rows = BenchmarkBaseline.Read(path);
            var outOfRange = rows
                .Where(row => row.Mean <= 0 || row.Allocated < 0)
                .Select(row => $"{row.Entry}: Mean={row.Mean}, Allocated={row.Allocated}")
                .ToList();
            await Assert.That(outOfRange).IsEmpty()
                .Because($"{path}: Mean must be positive and Allocated non-negative — a schema check, not a threshold");
        }
    }

    /// <summary>
    /// A2: a refused invoke is the same shape on every run and keeps no mutation, checked over at
    /// least three repeats so a first-run-only result cannot pass.
    /// </summary>
    [Test]
    public async Task InvokeBaseline_NamesEveryCase_AndEachCaseRunsOnceFailureAsDeclared() {
        await Assert.That(File.Exists(BenchmarkBaseline.InvokeCsvPath)).IsTrue()
            .Because($"{BenchmarkBaseline.InvokeCsvPath} is the committed invoke baseline; the command that produces it is in Poly.Benchmarks/TwoPhaseInvokeBenchmark.cs");

        var cases = TwoPhaseInvokeBenchmark.Cases().ToList();
        await Assert.That(cases.Count).IsGreaterThan(0)
            .Because("the invoke argument source must enumerate every action case");

        var rows = BenchmarkBaseline.Read(BenchmarkBaseline.InvokeCsvPath);
        var recorded = rows.Select(row => row.Entry).Order(StringComparer.Ordinal).ToList();
        var expected = cases.Select(entry => entry.Key).Order(StringComparer.Ordinal).ToList();
        await Assert.That(string.Join(", ", recorded)).IsEqualTo(string.Join(", ", expected))
            .Because("every invoke row must name a case, and every case must have a row");

        var foreign = rows
            .Where(row => row.Method != TwoPhaseInvokeBenchmark.Method)
            .Select(row => $"{row.Method} {row.Entry}")
            .ToList();
        await Assert.That(foreign).IsEmpty()
            .Because($"{BenchmarkBaseline.InvokeCsvPath} must hold only {TwoPhaseInvokeBenchmark.Method} rows");

        var benchmark = new TwoPhaseInvokeBenchmark();
        foreach (var entry in cases) {
            var transition = entry.Action == TwoPhaseInvokeBenchmark.TransitionAction;
            var idle = transition ? entry.Instance.Bag["Status"] : null;
            object? enRoute = null;
            for (var run = 1; run <= 3; run++) {
                if (transition && run > 1)
                    entry.Instance.Bag["Status"] = idle;
                var before = entry.Load();
                var shape = benchmark.TwoPhase_ActionInvoke(entry);
                await Assert.That(shape).IsEqualTo(entry.Expected)
                    .Because($"{entry.Key} run {run} must take its declared outcome shape, not only the first run");
                await Assert.That(entry.Load()).IsEqualTo(before)
                    .Because($"{entry.Key} run {run} must keep CurrentLoad unchanged; a kept mutation is the Q1 baseline, not a measurement");
                if (!transition)
                    continue;

                var status = entry.Instance.Bag["Status"];
                await Assert.That(status).IsNotEqualTo(idle)
                    .Because($"{entry.Key} run {run} must move Status off Idle, so it is an Idle→EnRoute transition and the reset before the next run is real");
                if (enRoute is null)
                    enRoute = status;
                else
                    await Assert.That(status).IsEqualTo(enRoute)
                        .Because($"{entry.Key} run {run} starts from Idle after the reset, so it is an Idle→EnRoute transition — not an EnRoute→EnRoute self-transition — and the entry effect must land Status on EnRoute again");
            }
        }
    }

    [Test]
    public async Task Baselines_PinExactHeaders_AndCoverSimulatorUniqueCase() {
        string[] paths = [
            BenchmarkBaseline.ParseCsvPath,
            BenchmarkBaseline.InvokeCsvPath,
            BenchmarkBaseline.AnalysisCsvPath,
            BenchmarkBaseline.SimulatorCsvPath,
        ];
        foreach (var path in paths) {
            await Assert.That(File.Exists(path)).IsTrue()
                .Because($"{path} is a committed baseline; each benchmark class doc names the command that produces it");

            var header = File.ReadLines(path).First();
            foreach (var required in BenchmarkBaseline.RequiredHeaders) {
                await Assert.That(header).Contains(required)
                    .Because($"{path} must pin the exact header '{required}', units included");
            }
            await Assert.That(header).DoesNotContain(",Mean,")
                .Because($"{path} must not carry a unitless Mean column");
            await Assert.That(header).DoesNotContain(",Allocated,")
                .Because($"{path} must not carry a unitless Allocated column");
        }

        var simulator = BenchmarkBaseline.Read(BenchmarkBaseline.SimulatorCsvPath);
        await Assert.That(simulator.Count).IsEqualTo(1)
            .Because("the simulator baseline is the single unique-port case");
        await Assert.That(simulator[0].Method).IsEqualTo(SimulatorInvokeBenchmark.Method);
        await Assert.That(simulator[0].Entry).IsEqualTo(SimulatorInvokeBenchmark.Key);
    }
}