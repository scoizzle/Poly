using System.Reflection;

using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

using StepResult = (bool Success, string? Message, object? Value);

namespace Poly.Tests.TestHelpers;

sealed class SimulateSide(Domain domain) : ParitySide(domain) {
    readonly DomainInstanceStore _store = new();

    protected override StepResult CreateCore(Entity model, (string Name, object? Value)[] values) {
        var properties = values.Where(v => v.Value is not IEnumerable<object>)
            .ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        var instance = DomainEntityInstance.Create(model, properties, Domain);
        _store.Add(instance);
        foreach (var (name, value) in values.Where(v => v.Value is IEnumerable<object>))
            foreach (var target in (IEnumerable<object>)value!)
                _store.Link(name, instance, (DomainEntityInstance)target);
        return (true, null, instance);
    }

    protected override StepResult InvokeCore(string action) {
        var result = ((DomainEntityInstance)Current).InvokeAction(action);
        return (result.Succeeded, result.ErrorMessage, null);
    }

    protected override object? EvaluateCore(string policy) {
        var instance = (DomainEntityInstance)Current;
        return instance.EvaluatePolicy(instance.Entity.Policies.FirstOrDefault(p => p.Name == policy)
            ?? throw new ArgumentException($"Entity '{instance.Entity.Name}' has no policy '{policy}'."));
    }

    protected override Dictionary<string, string?> StateOf(object entity, Entity model) {
        var instance = (DomainEntityInstance)entity;
        var state = model.Properties.ToDictionary(p => p.Name, p => instance.GetProperty<object>(p.Name)?.ToString());
        foreach (var nav in model.Navigations)
            state[nav.Name] = _store.GetLinkedTargets(nav.Name, instance).Count.ToString();
        state["Stage"] = instance.CurrentStage;
        return state;
    }
}

sealed class PrintedSide(Domain domain, Assembly assembly) : ParitySide(domain) {
    protected override StepResult CreateCore(Entity model, (string Name, object? Value)[] values) {
        var arguments = values.Select(v => (v.Name, v.Value is IEnumerable<object> list ? ToArray(list) : v.Value));
        return Unpack(ExportedCSharp.InvokeCreate(assembly.GetType(model.Name)!, [.. arguments]));
    }

    protected override StepResult InvokeCore(string action) => Unpack(Method(action).Invoke(Current, null)!);

    protected override object? EvaluateCore(string policy) => Method(policy).Invoke(Current, null);

    protected override Dictionary<string, string?> StateOf(object entity, Entity model) {
        var state = model.Properties.ToDictionary(p => p.Name, p => Read(entity, p.Name)?.ToString());
        foreach (var nav in model.Navigations)
            state[nav.Name] = Read(entity, nav.Name) is System.Collections.ICollection c ? c.Count.ToString() : "0";
        state["Stage"] = Read(entity, "CurrentStage")?.ToString();
        return state;
    }

    MethodInfo Method(string name) => Current.GetType().GetMethod(name, Type.EmptyTypes)
        ?? throw new ArgumentException($"Printed type '{Current.GetType().Name}' has no public '{name}()'.");

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
