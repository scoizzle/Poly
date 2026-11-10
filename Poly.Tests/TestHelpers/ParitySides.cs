using System.Reflection;

using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

using StepResult = (bool Success, string? Message, object? Value);

namespace Poly.Tests.TestHelpers;

sealed class SimulateSide(Domain domain) : ParitySide(domain) {
    readonly ModuleInstanceExecutor _exec = ModuleExec.For(domain);

    protected override StepResult CreateCore(Entity model, (string Name, object? Value)[] values) {
        var properties = values.Where(v => LinksOf(v.Value) is null)
            .ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        ModuleOutcome outcome;
        try {
            outcome = _exec.Create(model.Name, properties);
        }
        catch (ConstraintFailureException ex) {
            // Printed CreateCore records the same throw as a failed step, not an
            // exception type. Void entry effects throw on both sides.
            return (false, ex.Message, null);
        }
        // The factory only takes parameters it declares. A scenario may still pass a
        // property the entry effects own; the printed constructor accepts it, the
        // module factory does not. Drop those keys and create again.
        while (!outcome.Succeeded && outcome.Error is { } err) {
            var unknown = System.Text.RegularExpressions.Regex.Match(
                err, "Unknown property '([^']+)'");
            if (!unknown.Success || !properties.Remove(unknown.Groups[1].Value))
                break;
            outcome = _exec.Create(model.Name, properties);
        }
        if (!outcome.Succeeded || outcome.Instance is null)
            return (false, outcome.Error, null);
        var instance = outcome.Instance;
        foreach (var (name, value) in values) {
            var links = LinksOf(value);
            if (links is null)
                continue;
            foreach (var target in links)
                _exec.Link(name, instance, target);
        }
        return (true, null, instance);
    }

    protected override StepResult InvokeCore(string action, (string Name, object? Value)[] args) {
        IReadOnlyDictionary<string, object?>? dict = args.Length == 0
            ? null
            : args.ToDictionary(a => a.Name, a => a.Value, StringComparer.Ordinal);
        var result = _exec.Invoke((ModuleInstance)Current, action, dict);
        return (result.Succeeded, result.Error, null);
    }

    protected override object? EvaluateCore(string policy) =>
        _exec.EvaluatePolicy((ModuleInstance)Current, policy);

    protected override string TypeName(object entity) => ((ModuleInstance)entity).EntityName;

    protected override object LinkedTarget(string relationship, int index) {
        var targets = _exec.LinkedTargets(relationship, (ModuleInstance)Current);
        if (index < 0 || index >= targets.Count)
            throw new ArgumentException(
                $"'{relationship}' has {targets.Count} linked target(s); cannot use index {index}.");
        return targets[index];
    }

    protected override Dictionary<string, string?> StateOf(object entity, Entity model) {
        var instance = (ModuleInstance)entity;
        var state = model.Properties.ToDictionary(
            p => p.Name, p => instance.Bag.TryGetValue(p.Name, out var value) ? value?.ToString() : null);
        foreach (var nav in model.Navigations)
            state[nav.Name] = _exec.LinkedTargets(nav.Name, instance).Count.ToString();
        state["Stage"] = ModuleReads.Stage(instance)
            ?? (model.Stages.Count == 1 ? model.Stages[0].Name : null);
        return state;
    }

    static IReadOnlyList<ModuleInstance>? LinksOf(object? value) {
        if (value is ModuleInstance one)
            return [one];
        if (value is IEnumerable<object> many && many.All(item => item is ModuleInstance))
            return [.. many.Cast<ModuleInstance>()];
        return null;
    }
}

sealed class PrintedSide(Domain domain, Assembly assembly) : ParitySide(domain) {
    internal RecordingStoragePort Port { get; } = new();

    protected override StepResult CreateCore(Entity model, (string Name, object? Value)[] values) {
        var arguments = values.Select(v => {
            var value = v.Value is IEnumerable<object> list ? ToArray(list) : v.Value;
            var prop = model.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, v.Name, StringComparison.OrdinalIgnoreCase));
            var printed = prop is not null ? assembly.GetType(prop.Type.TypeName) : null;
            if (printed is not null)
                value = Materialize(value, printed);
            return (v.Name, value);
        }).Append(("db", (object?)Port));
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
        // The storage port is threaded into an action that touches a unique key; it is
        // the harness's, not a scenario argument.
        static bool IsPort(ParameterInfo p) => p.ParameterType == typeof(IStoragePort);
        var method = Current.GetType().GetMethods()
            .FirstOrDefault(m => m.Name == action && m.GetParameters().Count(p => !IsPort(p)) == args.Length)
            ?? throw new ArgumentException(
                $"Printed type '{Current.GetType().Name}' has no public '{action}' with {args.Length} parameter(s).");
        var parameters = method.GetParameters();
        var callArgs = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++) {
            if (IsPort(parameters[i])) {
                callArgs[i] = Port;
                continue;
            }
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

    protected override object LinkedTarget(string relationship, int index) {
        var value = Read(Current, relationship);
        if (value is System.Collections.IList list) {
            if (index < 0 || index >= list.Count)
                throw new ArgumentException(
                    $"'{relationship}' has {list.Count} linked target(s); cannot use index {index}.");
            return list[index]!;
        }
        if (value is null || index != 0)
            throw new ArgumentException(
                $"'{relationship}' has no linked target at index {index}.");
        return value;
    }

    protected override Dictionary<string, string?> StateOf(object entity, Entity model) {
        var state = model.Properties.ToDictionary(p => p.Name, p => Read(entity, p.Name)?.ToString());
        foreach (var nav in model.Navigations)
            state[nav.Name] = Read(entity, nav.Name) switch {
                System.Collections.ICollection c => c.Count.ToString(),
                null => "0",
                _ => "1", // a to-one navigation holding its target
            };
        // An implicit single stage has no printed CurrentStage; the value is
        // constant, so report the model's stage on both sides.
        var currentStage = Read(entity, "CurrentStage")?.ToString();
        state["Stage"] = currentStage
            ?? (model.Stages.Count == 1 ? model.Stages[0].Name : null);
        return state;
    }

    MethodInfo Method(string name) => Current.GetType().GetMethod(name, Type.EmptyTypes)
        ?? throw new ArgumentException($"Printed type '{Current.GetType().Name}' has no public '{name}()'.");

    /// <summary>A value outside every declared member, for a caller passing an out-of-range cast.</summary>
    const int UndefinedEnumMember = 42;

    static object? Materialize(object? value, Type target) {
        if (value is null || target.IsInstanceOfType(value))
            return value;
        var enumType = Nullable.GetUnderlyingType(target) ?? target;
        if (enumType.IsEnum) {
            // A defined member name parses. A non-member lands as an undefined member so the
            // module's own member guard runs, the way a hand-written caller passing an
            // out-of-range cast reaches it instead of failing in the harness. The CLR enum
            // cannot carry the original text, so the guard names the numeric value.
            if (value is string s) {
                if (Enum.TryParse(enumType, s, out var parsed) && parsed is not null)
                    return parsed;
                return Enum.ToObject(enumType, UndefinedEnumMember);
            }
            return Enum.ToObject(enumType, value);
        }
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

    // Printed methods return DomainResult or DomainResult<T>. A failed DomainResult<T>
    // throws on Value, so a failing row reads null instead.
    static StepResult Unpack(object result) {
        var type = result.GetType();
        var success = (bool)type.GetProperty("IsSuccess")!.GetValue(result)!;
        return (success,
            type.GetProperty("ErrorMessage")!.GetValue(result) as string,
            success ? type.GetProperty("Value")?.GetValue(result) : null);
    }
}