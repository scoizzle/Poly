using System.Reflection;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Runtime;

using StepResult = (bool Success, string? Message, object? Value);

namespace Poly.Tests.TestHelpers;

/// <summary>
/// What one step did: success or failure, its message, the exception type if it threw, the
/// entity's state afterwards, and the typed failure of a failed step.
/// <see cref="FailureKind"/>, <see cref="FailurePath"/> and <see cref="FailureGuard"/> are the
/// <c>Poly.Runtime.DomainFailure</c> of a step that failed through a result, on both sides.
/// A step that threw, or that failed without a typed failure, leaves them null.
/// The graph members are set only for a scenario that opts in with <c>recordGraph</c> (L-03c), and are
/// <see cref="GraphState"/> renderings: <see cref="GraphBefore"/> and <see cref="GraphAfter"/> render the graph before
/// and after the step against the table taken before it (an entity the step left behind is an orphan), on one host,
/// and <see cref="GraphParity"/> renders the graph after the step against a fresh table, in the form both hosts compare.
/// </summary>
public sealed record ParityOutcome(
    string Step, bool Success, string? Message, string? ExceptionType, IReadOnlyDictionary<string, string?> State,
    string? FailureKind = null, string? FailurePath = null, string? FailureGuard = null,
    string? GraphBefore = null, string? GraphAfter = null, string? GraphParity = null);

/// <summary>
/// One field that differs between the sides. <see cref="Field"/> is the ledger vocabulary:
/// <c>success</c>, <c>message</c>, <c>failureKind</c>, <c>failurePath</c>, <c>failureGuard</c>, <c>exceptionType</c>, <c>state:&lt;member&gt;</c>,
/// <c>graph</c> (a scenario that records the graph) or <c>stepCount</c>. <see cref="ToString"/> keeps the wording the existing rows assert on.
/// </summary>
public sealed record ParityDifference(int Index, string Step, string Field, string? Simulate, string? Printed) {
    public override string ToString() => Field switch {
        "stepCount" => $"step count differs (simulate {Simulate}, printed {Printed})",
        "success" => $"{Step}: success differs (simulate '{Simulate}', printed '{Printed}')",
        "message" => $"{Step}: failure message differs (simulate '{Simulate}', printed '{Printed}')",
        "failureKind" => $"{Step}: failure kind differs (simulate '{Simulate}', printed '{Printed}')",
        "failurePath" => $"{Step}: failure path differs (simulate '{Simulate}', printed '{Printed}')",
        "failureGuard" => $"{Step}: failure guard differs (simulate '{Simulate}', printed '{Printed}')",
        "exceptionType" => $"{Step}: exception type differs (simulate '{Simulate}', printed '{Printed}')",
        "graph" => $"{Step}: graph differs\n  simulate:\n{Simulate}  printed:\n{Printed}",
        _ => $"{Step}: '{Field.Replace("state:", "", StringComparison.Ordinal)}' differs (simulate '{Simulate}', printed '{Printed}')",
    };
}

/// <summary>
/// One session factory, two instances: the printed module and the simulate domain must be
/// compiled against the same libraries, and the simulator must not inherit the session that
/// lowers the printed side (lowering registers its session against the domain it was given).
/// </summary>
public static class ParitySessions {
    /// <summary>
    /// Core plus <paramref name="extensionIds"/>' libraries, then the storage library that
    /// threads an <see cref="IStoragePort"/> into Create and the actions. An empty list means
    /// the authoring seed, the same default <see cref="ModuleExec.For"/> uses.
    /// </summary>
    public static DomainSession Create(IReadOnlyList<string>? extensionIds = null) {
        var builder = SessionBuilder.CreateEmpty();
        builder.LoadCore();
        foreach (var id in extensionIds is { Count: > 0 } ? extensionIds : ExtensionCatalog.ProductAuthoring)
            if (!string.Equals(id, "persistence", StringComparison.Ordinal))
                builder.Load(ExtensionCatalog.Core.Resolve(id));
        builder.Load(new PersistenceEmitLibrary());
        return builder.Build();
    }
}

/// <summary>
/// Runs one scenario twice, once on the interpreter and once on the Roslyn-compiled printed C#,
/// and compares what every step did. The scenario is written once, against <see cref="ParitySide"/>.
/// </summary>
public sealed class ParityScenario {
    readonly Domain _domain;
    readonly Domain _printedDomain;
    readonly Assembly _assembly;
    readonly IReadOnlySet<string> _recordedExceptions;
    readonly bool _recordPostThrowState;
    readonly bool _recordGraph;

    ParityScenario(Domain domain, Domain printedDomain, Assembly assembly, IReadOnlySet<string> recordedExceptions,
        bool recordPostThrowState, bool recordGraph = false) {
        _domain = domain;
        _printedDomain = printedDomain;
        _assembly = assembly;
        _recordedExceptions = recordedExceptions;
        _recordPostThrowState = recordPostThrowState;
        _recordGraph = recordGraph;
    }

    /// <summary>The names of the sides the last <see cref="Run"/> executed, in order.</summary>
    public IReadOnlyList<string> Sides { get; private set; } = [];

    /// <summary>The session the simulator runs on: a factory instance of its own, not the lowering one.</summary>
    public DomainSession SimulateSession => RuntimeAnalysisCache.Session(_domain);

    /// <summary>The session that lowered the printed module.</summary>
    public DomainSession PrintedSession => RuntimeAnalysisCache.Session(_printedDomain);


    /// <summary>
    /// The simulate side runs the DSL's own domain and the printed side is the module the second
    /// factory instance lowers from <paramref name="printedDsl"/>. They are the same text in
    /// <see cref="FromDsl"/>. A test passes a deliberately different printed text to break the
    /// lowered module (a dropped guard, a dropped transition) and require the differential to go
    /// red; the domain facts the scenario reads come from <paramref name="simulateDsl"/>.
    /// <paramref name="recordedExceptions"/> names exception types (the printed C# defines its own
    /// types, so names, not <see cref="Type"/>) a scenario records as an outcome instead of a
    /// harness fault; the default set is unchanged for every existing row.
    /// <paramref name="recordPostThrowState"/> opts a scenario into reporting the current entity's
    /// state after a step throws. It is off by default because a row that used to compare two empty
    /// states would start comparing post-throw state, and every such flip is a finding for its own
    /// slice, not a harness change (see the loop-guard row in ExitBlockTransitionTests).
    /// <paramref name="recordGraph"/> opts a scenario into the <see cref="GraphState"/> members of every outcome and the
    /// <c>graph</c> difference field (L-03c). It is off by default, so no existing row compares graphs.
    /// </summary>
    public static ParityScenario FromDsls(
        string simulateDsl, string printedDsl, string assemblyName,
        string[]? recordedExceptions = null, bool recordPostThrowState = false, bool recordGraph = false) {
        var simulate = EvolvedDomain.FromDsl(simulateDsl).Domain;
        var ids = simulate.Extensions.Count > 0 ? simulate.Extensions : ExtensionCatalog.ProductAuthoring;

        var simulateSession = ParitySessions.Create(ids);
        RuntimeAnalysisCache.Bind(simulate, simulateSession);

        var printedSession = ParitySessions.Create(ids);
        var changes = new PolyDslParser(printedDsl, printedSession).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: printedSession);
        if (!evolved.Succeeded)
            throw new InvalidOperationException(string.Join("; ", evolved.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        var cs = new CSharpGenerator().Generate(printedSession.Lower(evolved.Root!, evolved.Analysis));
        return new(simulate, evolved.Root!, ExportedCSharp.CompileAndLoad(cs, assemblyName),
            new HashSet<string>(recordedExceptions ?? [], StringComparer.Ordinal), recordPostThrowState, recordGraph);
    }

    public static ParityScenario FromDsl(
        string dsl, string assemblyName, string[]? recordedExceptions = null, bool recordPostThrowState = false,
        bool recordGraph = false) =>
        FromDsls(dsl, dsl, assemblyName, recordedExceptions, recordPostThrowState, recordGraph);

    /// <summary>For domains built or edited through the API, which the DSL cannot always express. No storage library: such a domain holds no unique key.</summary>
    public static ParityScenario FromDomain(Domain domain, string assemblyName) {
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var cs = new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis));
        return new(domain, domain, ExportedCSharp.CompileAndLoad(cs, assemblyName), new HashSet<string>(StringComparer.Ordinal), false);
    }

    public (IReadOnlyList<ParityOutcome> Simulate, IReadOnlyList<ParityOutcome> Printed) Run(Action<ParitySide> steps) {
        var simulate = new SimulateSide(_domain, _recordedExceptions, _recordPostThrowState, _recordGraph);
        var printed = new PrintedSide(_domain, _assembly, _recordedExceptions, _recordPostThrowState, _recordGraph);
        Sides = [simulate.Name, printed.Name];
        steps(simulate);
        steps(printed);
        return (simulate.Outcomes, printed.Outcomes);
    }

    /// <summary>
    /// Fails if any step differs between the sides, naming the step and field. Returns the simulate outcomes.
    /// The two sides can share lowered code, so a row should also check the outcome it expects.
    /// </summary>
    public async Task<IReadOnlyList<ParityOutcome>> AssertAgree(Action<ParitySide> steps) {
        var (simulate, printed) = Run(steps);
        await Assert.That(Differences(simulate, printed)).IsEmpty();
        return simulate;
    }

    /// <summary>One line per difference between the sides; empty when they agree.</summary>
    public static List<string> Differences(IReadOnlyList<ParityOutcome> simulate, IReadOnlyList<ParityOutcome> printed) =>
        [.. DifferenceSet(simulate, printed).Select(difference => difference.ToString())];

    /// <summary>
    /// The differences as records, so a ledger entry can match an exact set of
    /// <c>(index, step, field)</c> keys with their values. The step count is one entry at index -1.
    /// </summary>
    public static IReadOnlyList<ParityDifference> DifferenceSet(
        IReadOnlyList<ParityOutcome> simulate, IReadOnlyList<ParityOutcome> printed) {
        var differences = new List<ParityDifference>();
        if (simulate.Count != printed.Count)
            differences.Add(new(-1, "(all)", "stepCount", simulate.Count.ToString(), printed.Count.ToString()));
        for (var i = 0; i < Math.Min(simulate.Count, printed.Count); i++) {
            var (s, p) = (simulate[i], printed[i]);
            void Compare(string field, string? simulated, string? printedValue) {
                if (simulated != printedValue)
                    differences.Add(new(i, s.Step, field, simulated, printedValue));
            }
            Compare("success", s.Success.ToString(), p.Success.ToString());
            Compare("message", s.Message, p.Message);
            Compare("failureKind", s.FailureKind, p.FailureKind);
            Compare("failurePath", s.FailurePath, p.FailurePath);
            Compare("failureGuard", s.FailureGuard, p.FailureGuard);
            Compare("exceptionType", s.ExceptionType, p.ExceptionType);
            foreach (var member in s.State.Keys.Union(p.State.Keys))
                Compare($"state:{member}", s.State.TryGetValue(member, out var a) ? a : "(absent)", p.State.TryGetValue(member, out var b) ? b : "(absent)");
            if (s.GraphParity is not null || p.GraphParity is not null)
                Compare("graph", s.GraphParity, p.GraphParity);
        }
        return differences;
    }
}

/// <summary>
/// One implementation of the scenario's steps; each step records a <see cref="ParityOutcome"/>.
/// A step that throws <see cref="InvalidOperationException"/> or
/// <see cref="ConstraintFailureException"/> is an outcome, as is a type the scenario opts into by
/// name; any other exception is a harness fault and fails the row.
/// </summary>
public abstract class ParitySide(
    Domain domain, IReadOnlySet<string>? recordedExceptions = null, bool recordPostThrowState = false,
    bool recordGraph = false) {
    public List<ParityOutcome> Outcomes { get; } = [];
    protected Domain Domain { get; } = domain;
    Entity? _model;
    object? _current;
    readonly List<object> _roots = [];

    /// <summary>The side's name, so a ledger entry validates against the sides that actually ran.</summary>
    public abstract string Name { get; }

    protected object Current => _current ?? throw new ArgumentException("Create an entity before invoking or evaluating on it.");

    /// <summary>Creates an entity. A value that is an earlier <see cref="Create"/> result, or a list of them, links those entities.</summary>
    public object? Create(string type, params (string Name, object? Value)[] values) {
        var model = Domain.Types.OfType<Entity>().FirstOrDefault(e => e.Name == type)
            ?? throw new ArgumentException($"No entity '{type}' in the domain.");
        var created = Record($"create {type}", () => CreateCore(model, values), model);
        if (created is not null) {
            (_current, _model) = (created, model);
            _roots.Add(GraphRoot(created));
        }
        return created;
    }

    /// <summary>Makes an earlier <see cref="Create"/> result the current entity and records its state.</summary>
    public void Use(object? entity) {
        if (entity is null) throw new ArgumentException("Cannot use an entity whose create failed.");
        var type = TypeName(entity);
        (_current, _model) = (entity, Domain.Types.OfType<Entity>().Single(e => e.Name == type));
        Record($"use {type}", () => (true, null, null));
    }

    /// <summary>
    /// Makes the linked target of a navigation the current entity and records its state,
    /// so a test can assert the child's side of a link rather than only the source's.
    /// </summary>
    public void UseLinked(string relationship, int index = 0) {
        var target = LinkedTarget(relationship, index);
        var type = TypeName(target);
        (_current, _model) = (target, Domain.Types.OfType<Entity>().Single(e => e.Name == type));
        Record($"use {relationship}[{index}]", () => (true, null, null));
    }

    /// <summary>
    /// Links the current entity to already-created entities, for a graph a create argument cannot
    /// express (a cycle needs both entities before either link).
    /// </summary>
    public void Link(string relationship, params object?[] targets) =>
        Record($"link {relationship}", () => LinkCore(relationship, targets));

    public void Invoke(string action, params (string Name, object? Value)[] args) =>
        Record($"invoke {action}", () => InvokeCore(action, args));

    /// <summary>
    /// Invokes an action with no host-side rollback: on the simulator the module's wrapper runs on the bag graph
    /// without the executor's transaction (<see cref="ModuleScopeOnly"/>), so only the module's action scope can
    /// restore what a failure touched. The printed host has no other mechanism, so it invokes as usual. Recorded under
    /// the same step name as <see cref="Invoke"/>.
    /// </summary>
    public void InvokeScopeOnly(string action, params (string Name, object? Value)[] args) =>
        Record($"invoke {action}", () => InvokeScopeOnlyCore(action, args));

    /// <summary>Evaluates a policy; its answer is recorded in the state under the policy's name.</summary>
    public void EvaluatePolicy(string policy) =>
        Record($"policy {policy}", () => (true, null, EvaluateCore(policy)), answerName: policy);

    protected abstract StepResult CreateCore(Entity model, (string Name, object? Value)[] values);
    protected abstract StepResult InvokeCore(string action, (string Name, object? Value)[] args);
    protected virtual StepResult InvokeScopeOnlyCore(string action, (string Name, object? Value)[] args) => InvokeCore(action, args);
    /// <summary>How this host's entities read for <see cref="GraphState"/>.</summary>
    protected abstract GraphState.Reader GraphReader { get; }
    /// <summary>The object <see cref="GraphState"/> walks for a <see cref="Create"/> result.</summary>
    protected virtual object GraphRoot(object entity) => entity;
    protected abstract StepResult LinkCore(string relationship, object?[] targets);
    protected abstract object? EvaluateCore(string policy);
    protected abstract object LinkedTarget(string relationship, int index);
    protected abstract Dictionary<string, string?> StateOf(object entity, Entity model);
    protected abstract string TypeName(object entity);

    /// <summary>The typed failure of the step a core just ran, or null; <see cref="Record"/> reads and clears it.</summary>
    protected DomainFailure? LastFailure { get; set; }

    object? Record(string step, Func<StepResult> act, Entity? created = null, string? answerName = null) {
        LastFailure = null;
        var table = recordGraph ? GraphState.Table(GraphReader, _roots) : null;
        var before = table is null ? null : GraphState.Render(GraphReader, table);
        try {
            var (success, message, value) = act();
            var failure = success ? null : LastFailure;
            // A failed create has no entity, so its state is empty; other steps report the current entity.
            var subject = created is null ? Current : value;
            var state = subject is null ? [] : StateOf(subject, created ?? _model!);
            if (answerName is not null) state[answerName] = value?.ToString();
            var (after, parity) = Graphs(table, created is not null && success ? value : null);
            Outcomes.Add(new(step, success, message, null, state, failure?.Kind.ToString(), failure?.ElementPath, failure?.Guard,
                before, after, parity));
            return success ? value : null;
        }
        catch (Exception ex) when (IsRecordedOutcome(Unwrap(ex))) {
            var thrown = Unwrap(ex);
            var (after, parity) = Graphs(table, null);
            Outcomes.Add(new(step, false, thrown.Message, thrown.GetType().Name, StateAfterThrow(created),
                GraphBefore: before, GraphAfter: after, GraphParity: parity));
            return null;
        }
    }

    // The held rendering uses the table from before the step; the parity rendering walks a fresh table, which includes
    // the entity a create step just made.
    (string? After, string? Parity) Graphs(IReadOnlyList<object>? table, object? created) {
        if (table is null)
            return (null, null);
        IEnumerable<object> roots = created is null ? _roots : [.. _roots, GraphRoot(created)];
        return (GraphState.Render(GraphReader, table), GraphState.Of(GraphReader, roots, parity: true));
    }

    /// <summary>
    /// A scenario that opts in reports the state its throwing step left behind: the subject exists,
    /// so its state is readable even though the step failed. A failed create has no entity of its
    /// own, so it still reports none. The default is unchanged (an empty state for a throw), because
    /// turning this on everywhere would flip rows that compare two empty states today.
    /// </summary>
    IReadOnlyDictionary<string, string?> StateAfterThrow(Entity? created) =>
        !recordPostThrowState || created is not null || _current is null || _model is null
            ? new Dictionary<string, string?>()
            : StateOf(_current, _model);

    static Exception Unwrap(Exception ex) => ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;

    bool IsRecordedOutcome(Exception ex) => IsRecordedByDefault(ex) || (recordedExceptions?.Contains(ex.GetType().Name) ?? false);

    // Printed C# defines its own ConstraintFailureException type, so match by name.
    static bool IsRecordedByDefault(Exception ex) =>
        ex is InvalidOperationException
        || string.Equals(ex.GetType().Name, nameof(ConstraintFailureException), StringComparison.Ordinal);
}