using System.Reflection;

using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

using StepResult = (bool Success, string? Message, object? Value);

namespace Poly.Tests.TestHelpers;

/// <summary>What one step did: success or failure, its message, the exception type if it threw, and the entity's state afterwards.</summary>
public sealed record ParityOutcome(
    string Step, bool Success, string? Message, string? ExceptionType, IReadOnlyDictionary<string, string?> State);

/// <summary>
/// Runs one scenario twice, once on the interpreter and once on the Roslyn-compiled printed C#,
/// and compares what every step did. The scenario is written once, against <see cref="ParitySide"/>.
/// </summary>
public sealed class ParityScenario {
    readonly Domain _domain;
    readonly Assembly _assembly;

    ParityScenario(Domain domain, Assembly assembly) {
        _domain = domain;
        _assembly = assembly;
    }

    public static ParityScenario FromDsl(string dsl, string assemblyName) =>
        FromDomain(EvolvedDomain.FromDsl(dsl).Domain, assemblyName);

    /// <summary>For domains built or edited through the API, which the DSL cannot always express.</summary>
    public static ParityScenario FromDomain(Domain domain, string assemblyName) {
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        return new(domain, ExportedCSharp.CompileAndLoad(cs, assemblyName));
    }

    public (IReadOnlyList<ParityOutcome> Simulate, IReadOnlyList<ParityOutcome> Printed) Run(Action<ParitySide> steps) {
        var simulate = new SimulateSide(_domain);
        var printed = new PrintedSide(_domain, _assembly);
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
    public static List<string> Differences(IReadOnlyList<ParityOutcome> simulate, IReadOnlyList<ParityOutcome> printed) {
        var differences = new List<string>();
        if (simulate.Count != printed.Count)
            differences.Add($"step count differs (simulate {simulate.Count}, printed {printed.Count})");
        foreach (var (s, p) in simulate.Zip(printed)) {
            void Compare(string field, string? simulated, string? printedValue) {
                if (simulated != printedValue)
                    differences.Add($"{s.Step}: {field} differs (simulate '{simulated}', printed '{printedValue}')");
            }
            Compare("success", s.Success.ToString(), p.Success.ToString());
            Compare("failure message", s.Message, p.Message);
            Compare("exception type", s.ExceptionType, p.ExceptionType);
            foreach (var member in s.State.Keys.Union(p.State.Keys))
                Compare($"'{member}'", s.State.TryGetValue(member, out var a) ? a : "(absent)", p.State.TryGetValue(member, out var b) ? b : "(absent)");
        }
        return differences;
    }
}

/// <summary>
/// One implementation of the scenario's steps; each step records a <see cref="ParityOutcome"/>.
/// A step that throws <see cref="InvalidOperationException"/> is an outcome; any other exception is a harness fault and fails the row.
/// </summary>
public abstract class ParitySide(Domain domain) {
    public List<ParityOutcome> Outcomes { get; } = [];
    protected Domain Domain { get; } = domain;
    Entity? _model;
    object? _current;

    protected object Current => _current ?? throw new ArgumentException("Create an entity before invoking or evaluating on it.");

    /// <summary>Creates an entity. A value that is a list of earlier <see cref="Create"/> results links those entities.</summary>
    public object? Create(string type, params (string Name, object? Value)[] values) {
        var model = Domain.Types.OfType<Entity>().FirstOrDefault(e => e.Name == type)
            ?? throw new ArgumentException($"No entity '{type}' in the domain.");
        var created = Record($"create {type}", () => CreateCore(model, values), model);
        if (created is not null) (_current, _model) = (created, model);
        return created;
    }

    public void Invoke(string action) => Record($"invoke {action}", () => InvokeCore(action));

    /// <summary>Evaluates a policy; its answer is recorded in the state under the policy's name.</summary>
    public void EvaluatePolicy(string policy) =>
        Record($"policy {policy}", () => (true, null, EvaluateCore(policy)), answerName: policy);

    protected abstract StepResult CreateCore(Entity model, (string Name, object? Value)[] values);
    protected abstract StepResult InvokeCore(string action);
    protected abstract object? EvaluateCore(string policy);
    protected abstract Dictionary<string, string?> StateOf(object entity, Entity model);

    object? Record(string step, Func<StepResult> act, Entity? created = null, string? answerName = null) {
        try {
            var (success, message, value) = act();
            // A failed create has no entity, so its state is empty; other steps report the current entity.
            var subject = created is null ? Current : value;
            var state = subject is null ? [] : StateOf(subject, created ?? _model!);
            if (answerName is not null) state[answerName] = value?.ToString();
            Outcomes.Add(new(step, success, message, null, state));
            return success ? value : null;
        }
        catch (Exception ex) when (Unwrap(ex) is InvalidOperationException thrown) {
            Outcomes.Add(new(step, false, thrown.Message, thrown.GetType().Name, new Dictionary<string, string?>()));
            return null;
        }
    }

    static Exception Unwrap(Exception ex) => ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
}
