using System.Reflection;

using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

using StepResult = (bool Success, string? Message, object? Value);

namespace Poly.Tests.TestHelpers;

sealed class SimulateSide(Domain domain) : ParitySide(domain) {
    readonly DomainInstanceStore _store = new();

    protected override StepResult CreateCore(Entity model, (string Name, object? Value)[] values) {
        var properties = values.Where(v => v.Value is not (IEnumerable<object> or DomainEntityInstance))
            .ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        DomainEntityInstance instance;
        try {
            instance = DomainEntityInstance.Create(model, properties, Domain);
        }
        catch (ConstraintFailureException ex) {
            return (false, ex.Message, null);
        }
        _store.Add(instance);
        foreach (var (name, value) in values.Where(v => v.Value is IEnumerable<object> or DomainEntityInstance))
            foreach (var target in value is DomainEntityInstance one ? [one] : (IEnumerable<object>)value!)
                _store.Link(name, instance, (DomainEntityInstance)target);
        return (true, null, instance);
    }

    protected override StepResult InvokeCore(string action, (string Name, object? Value)[] args) {
        IReadOnlyDictionary<string, object?>? dict = args.Length == 0
            ? null
            : args.ToDictionary(a => a.Name, a => a.Value, StringComparer.Ordinal);
        var result = ((DomainEntityInstance)Current).InvokeAction(action, dict);
        return (result.Succeeded, result.ErrorMessage, null);
    }

    protected override object? EvaluateCore(string policy) {
        var instance = (DomainEntityInstance)Current;
        return instance.EvaluatePolicy(instance.Entity.Policies.FirstOrDefault(p => p.Name == policy)
            ?? throw new ArgumentException($"Entity '{instance.Entity.Name}' has no policy '{policy}'."));
    }

    protected override string TypeName(object entity) => ((DomainEntityInstance)entity).Entity.Name;

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
        var arguments = values.Select(v => {
            var value = v.Value is IEnumerable<object> list ? ToArray(list) : v.Value;
            var prop = model.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, v.Name, StringComparison.Ordinal));
            var printed = prop is not null ? assembly.GetType(prop.Type.TypeName) : null;
            if (printed is not null)
                value = Materialize(value, printed);
            return (v.Name, value);
        });
        try {
            return Unpack(ExportedCSharp.InvokeCreate(assembly.GetType(model.Name)!, [.. arguments]));
        }
        catch (TargetInvocationException ex) when (
            ex.InnerException is { } inner
            && string.Equals(inner.GetType().Name, nameof(ConstraintFailureException), StringComparison.Ordinal)) {
            return (false, inner.Message, null);
        }
    }

    protected override StepResult InvokeCore(string action, (string Name, object? Value)[] args) {
        if (args.Length == 0)
            return Unpack(Method(action).Invoke(Current, null)!);
        var method = Current.GetType().GetMethods()
            .FirstOrDefault(m => m.Name == action && m.GetParameters().Length == args.Length)
            ?? throw new ArgumentException(
                $"Printed type '{Current.GetType().Name}' has no public '{action}' with {args.Length} parameter(s).");
        var parameters = method.GetParameters();
        var callArgs = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++) {
            var match = args.FirstOrDefault(a =>
                string.Equals(a.Name, parameters[i].Name, StringComparison.OrdinalIgnoreCase));
            if (match.Name is null)
                throw new ArgumentException($"Missing argument '{parameters[i].Name}' for '{action}'.");
            callArgs[i] = Materialize(match.Value, parameters[i].ParameterType);
        }
        return Unpack(method.Invoke(Current, callArgs)!);
    }

    protected override object? EvaluateCore(string policy) => Method(policy).Invoke(Current, null);

    protected override string TypeName(object entity) => entity.GetType().Name;

    protected override Dictionary<string, string?> StateOf(object entity, Entity model) {
        var state = model.Properties.ToDictionary(p => p.Name, p => Read(entity, p.Name)?.ToString());
        foreach (var nav in model.Navigations)
            state[nav.Name] = Read(entity, nav.Name) switch {
                System.Collections.ICollection c => c.Count.ToString(),
                null => "0",
                _ => "1", // a to-one navigation holding its target
            };
        state["Stage"] = Read(entity, "CurrentStage")?.ToString();
        return state;
    }

    MethodInfo Method(string name) => Current.GetType().GetMethod(name, Type.EmptyTypes)
        ?? throw new ArgumentException($"Printed type '{Current.GetType().Name}' has no public '{name}()'.");

    static object? Materialize(object? value, Type target) {
        if (value is null || target.IsInstanceOfType(value))
            return value;
        var enumType = Nullable.GetUnderlyingType(target) ?? target;
        if (value is string s && enumType.IsEnum)
            return Enum.Parse(enumType, s);
        if (value is not Dictionary<string, object?> fields)
            return value;
        var instance = Activator.CreateInstance(target)
            ?? throw new ArgumentException($"Could not create '{target.Name}' for invoke argument.");
        foreach (var (name, field) in fields) {
            var prop = target.GetProperty(name)
                ?? throw new ArgumentException($"'{target.Name}' has no property '{name}'.");
            prop.SetValue(instance, field);
        }
        return instance;
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
