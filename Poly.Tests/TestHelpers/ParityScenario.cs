using System.Reflection;

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
/// and asserts every step ended the same way. The scenario is written once, against <see cref="ParitySide"/>.
/// </summary>
public sealed class ParityScenario {
    readonly Domain _domain;
    readonly Assembly _assembly;

    ParityScenario(Domain domain, Assembly assembly) {
        _domain = domain;
        _assembly = assembly;
    }

    public static ParityScenario FromDsl(string dsl, string assemblyName) {
        var (domain, analysis) = EvolvedDomain.FromDsl(dsl);
        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        return new(domain, ExportedCSharp.CompileAndLoad(cs, assemblyName));
    }

    /// <summary>
    /// Runs <paramref name="steps"/> on both sides and compares each step's outcome field by field.
    /// Returns the outcomes so a test can also check the scenario did what it meant to.
    /// </summary>
    public async Task<IReadOnlyList<ParityOutcome>> AssertAgree(Action<ParitySide> steps) {
        var simulate = new SimulateSide(_domain);
        var printed = new PrintedSide(_domain, _assembly);
        steps(simulate);
        steps(printed);
        await Assert.That(printed.Outcomes.Count).IsEqualTo(simulate.Outcomes.Count);
        foreach (var (s, p) in simulate.Outcomes.Zip(printed.Outcomes)) {
            await Assert.That(p.Success).IsEqualTo(s.Success).Because($"{s.Step}: success differs");
            await Assert.That(p.Message).IsEqualTo(s.Message).Because($"{s.Step}: failure message differs");
            await Assert.That(p.ExceptionType).IsEqualTo(s.ExceptionType).Because($"{s.Step}: exception type differs");
            await Assert.That(p.State.Keys.Order()).IsEquivalentTo(s.State.Keys.Order()).Because($"{s.Step}: state members differ");
            foreach (var (name, value) in s.State)
                await Assert.That(p.State[name]).IsEqualTo(value).Because($"{s.Step}: '{name}' differs");
        }
        return simulate.Outcomes;
    }
}

/// <summary>One implementation of the scenario's steps. <see cref="Current"/> is the last entity created.</summary>
public abstract class ParitySide {
    public List<ParityOutcome> Outcomes { get; } = [];
    protected object? Current;

    /// <summary>Creates an entity; a value that is a list of earlier <see cref="Create"/> results links those entities.</summary>
    public object? Create(string type, params (string Name, object? Value)[] values) {
        var created = Record($"create {type}", () => CreateCore(type, values));
        Current = created ?? Current;
        return created;
    }

    public void Invoke(string action) => Record($"invoke {action}", () => InvokeCore(action));

    /// <summary>Evaluates a policy; its answer is recorded in the state as the policy's name.</summary>
    public void EvaluatePolicy(string policy) =>
        Record($"policy {policy}", () => (true, null, EvaluateCore(policy)), answerName: policy);

    protected abstract StepResult CreateCore(string type, (string Name, object? Value)[] values);
    protected abstract StepResult InvokeCore(string action);
    protected abstract object? EvaluateCore(string policy);
    protected abstract Dictionary<string, string?> StateOf(object entity);

    // Records the outcome; returns the step's value if it succeeded.
    object? Record(string step, Func<StepResult> act, string? answerName = null) {
        try {
            var (success, message, value) = act();
            var subject = Current ?? value;
            var state = subject is null ? [] : StateOf(subject);
            if (answerName is not null) state[answerName] = value?.ToString();
            Outcomes.Add(new(step, success, message, null, state));
            return success ? value : null;
        }
        catch (Exception ex) {
            var thrown = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            Outcomes.Add(new(step, false, thrown.Message, thrown.GetType().Name, new Dictionary<string, string?>()));
            return null;
        }
    }
}

sealed class SimulateSide(Domain domain) : ParitySide {
    readonly DomainInstanceStore _store = new();

    protected override StepResult CreateCore(string type, (string Name, object? Value)[] values) {
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == type);
        var properties = values.Where(v => v.Value is not IEnumerable<object>)
            .ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        var instance = DomainEntityInstance.Create(entity, properties, domain);
        _store.Add(instance);
        foreach (var (name, value) in values.Where(v => v.Value is IEnumerable<object>))
            foreach (var target in (IEnumerable<object>)value!)
                _store.Link(name, instance, (DomainEntityInstance)target);
        return (true, null, instance);
    }

    protected override StepResult InvokeCore(string action) {
        var result = ((DomainEntityInstance)Current!).InvokeAction(action);
        return (result.Succeeded, result.ErrorMessage, null);
    }

    protected override object? EvaluateCore(string policy) {
        var instance = (DomainEntityInstance)Current!;
        return instance.EvaluatePolicy(instance.Entity.Policies.First(p => p.Name == policy));
    }

    protected override Dictionary<string, string?> StateOf(object entity) {
        var instance = (DomainEntityInstance)entity;
        var state = instance.Entity.Properties.ToDictionary(p => p.Name, p => instance.GetProperty<object>(p.Name)?.ToString());
        foreach (var nav in instance.Entity.Navigations)
            state[nav.Name] = _store.GetLinkedTargets(nav.Name, instance).Count.ToString();
        state["Stage"] = instance.CurrentStage;
        return state;
    }
}

sealed class PrintedSide(Domain domain, Assembly assembly) : ParitySide {
    protected override StepResult CreateCore(string type, (string Name, object? Value)[] values) {
        var arguments = values.Select(v => (v.Name, v.Value is IEnumerable<object> list ? ToArray(list) : v.Value));
        return Unpack(ExportedCSharp.InvokeCreate(assembly.GetType(type)!, [.. arguments]));
    }

    protected override StepResult InvokeCore(string action) =>
        Unpack(Current!.GetType().GetMethod(action, Type.EmptyTypes)!.Invoke(Current, null)!);

    protected override object? EvaluateCore(string policy) =>
        Current!.GetType().GetMethod(policy, Type.EmptyTypes)!.Invoke(Current, null);

    protected override Dictionary<string, string?> StateOf(object entity) {
        var type = entity.GetType();
        var entityModel = domain.Types.OfType<Entity>().First(e => e.Name == type.Name);
        var state = entityModel.Properties.ToDictionary(p => p.Name, p => Read(entity, p.Name)?.ToString());
        foreach (var nav in entityModel.Navigations)
            state[nav.Name] = Read(entity, nav.Name) is System.Collections.ICollection c ? c.Count.ToString() : "0";
        state["Stage"] = Read(entity, "CurrentStage")?.ToString();
        return state;
    }

    // Printed members are public; names match ignoring case because navigations print in PascalCase. An entity without stages has no CurrentStage.
    static object? Read(object entity, string name) => entity.GetType().GetProperties()
        .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.GetValue(entity);

    static Array ToArray(IEnumerable<object> items) {
        var list = items.ToList();
        var array = Array.CreateInstance(list[0].GetType(), list.Count);
        for (var i = 0; i < list.Count; i++) array.SetValue(list[i], i);
        return array;
    }

    // Printed methods return DomainResult or DomainResult<T>.
    static StepResult Unpack(object result) {
        var type = result.GetType();
        return ((bool)type.GetProperty("IsSuccess")!.GetValue(result)!,
            type.GetProperty("ErrorMessage")!.GetValue(result) as string,
            type.GetProperty("Value")?.GetValue(result));
    }
}
