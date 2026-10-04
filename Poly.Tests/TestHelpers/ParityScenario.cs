using System.Collections;
using System.Reflection;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.TestHelpers;

public sealed record ParityOutcome(
    bool Success, string? FailureMessage, string? ExceptionType,
    IReadOnlyDictionary<string, object?> Properties, string? Stage);
public sealed record ParityHandle(DomainEntityInstance Sim, object Print);
public sealed record ParityPair(ParityOutcome Simulate, ParityOutcome Printed) {
    public ParityHandle? Entity { get; init; }
    public Task AssertAgree() => ParityScenario.AssertEqual(Simulate, Printed);
}

// Create / invoke / policy on interpreter and printed C#; compare success, message, exception, stage, properties.
public sealed class ParityScenario {
    readonly Domain _domain;
    readonly DomainInstanceStore _store = new();
    readonly Assembly _asm;
    DomainEntityInstance? _sim;
    object? _print;

    ParityScenario(Domain domain, Assembly asm) { _domain = domain; _asm = asm; }

    public static ParityScenario FromDsl(string dsl, string assemblyName) {
        var (domain, analysis) = EvolvedDomain.FromDsl(dsl);
        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        return new(domain, ExportedCSharp.CompileAndLoad(cs, assemblyName));
    }

    Entity Ent(string n) => _domain.Types.OfType<Entity>().First(e => e.Name == n);

    public ParityPair Create(string typeName, params (string Name, object? Value)[] named) {
        var e = Ent(typeName);
        var sim = Capture(() => {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (name, value) in named) {
                if (Handles(value).Any() || Nav(e, name) is not null) continue;
                var prop = e.Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (prop is not null) values[prop.Name] = value;
            }
            var inst = DomainEntityInstance.Create(e, values, _domain);
            _store.Add(inst);
            foreach (var (name, value) in named)
                foreach (var h in Handles(value))
                    if (Nav(e, name) is { } rel) _store.Link(rel.Name, inst, h.Sim);
            _sim = inst;
            return inst;
        }, e, false);
        var print = Capture(() => ExportedCSharp.InvokeCreate(
            _asm.GetType(typeName) ?? throw new InvalidOperationException($"Type '{typeName}' not found."),
            [.. named.Select(n => (n.Name, PrintArg(n.Value)))]), e, true);
        return new(sim, print) { Entity = _sim is not null && _print is not null && _sim.Entity.Name == e.Name ? new(_sim, _print) : null };
    }

    public ParityPair Invoke(string actionName, params (string Name, object? Value)[] args) {
        var e = _sim?.Entity ?? throw new InvalidOperationException("Create an entity before Invoke.");
        var sim = Capture(() => _sim!.InvokeAction(actionName, args.Length == 0 ? null
            : args.ToDictionary(a => a.Name, a => a.Value is ParityHandle h ? (object?)h.Sim : a.Value, StringComparer.Ordinal)), e, false, actionName);
        var print = Capture(() => Call(_print!, actionName, args), e, true, actionName);
        return new(sim, print) { Entity = _sim is not null && _print is not null ? new(_sim, _print) : null };
    }

    public ParityPair EvaluatePolicy(string policyName) {
        var e = _sim?.Entity ?? throw new InvalidOperationException("Create an entity before EvaluatePolicy.");
        var sim = Capture(() => _sim!.EvaluatePolicy(e.Policies.First(p => p.Name == policyName)), e, false, policyName: policyName);
        var print = Capture(() => _print!.GetType().GetMethod(policyName)!.Invoke(_print, null), e, true, policyName: policyName);
        return new(sim, print) { Entity = _sim is not null && _print is not null ? new(_sim, _print) : null };
    }

    // Field-by-field so a red row names the side and property that disagree.
    public static async Task AssertEqual(ParityOutcome a, ParityOutcome b) {
        await Assert.That(a.Success).IsEqualTo(b.Success).Because("success differs (simulate vs printed)");
        await Assert.That(a.FailureMessage).IsEqualTo(b.FailureMessage).Because("failure message differs (simulate vs printed)");
        await Assert.That(a.ExceptionType).IsEqualTo(b.ExceptionType).Because("exception type differs (simulate vs printed)");
        await Assert.That(a.Stage).IsEqualTo(b.Stage).Because("stage differs (simulate vs printed)");
        foreach (var key in a.Properties.Keys.Concat(b.Properties.Keys).Distinct(StringComparer.OrdinalIgnoreCase)) {
            a.Properties.TryGetValue(key, out var sv);
            b.Properties.TryGetValue(key, out var pv);
            await Assert.That(sv).IsEqualTo(pv).Because($"property '{key}' differs (simulate vs printed)");
        }
    }

    ParityOutcome Capture(Func<object?> act, Entity entity, bool printed, string? actionName = null, string? policyName = null) {
        try {
            var raw = act();
            if (raw is DomainEntityInstance created)
                return new(true, null, null, Snap(created, null, entity), created.CurrentStage);
            if (raw is bool flag && policyName is not null) {
                var props = Snap(printed ? null : _sim, printed ? _print : null, entity);
                props[policyName] = flag;
                return new(true, null, null, props, StageOf(printed ? _print : _sim));
            }
            var t = raw!.GetType();
            var okProp = t.GetProperty("IsSuccess") ?? t.GetProperty("Succeeded");
            if (okProp is not null) {
                var ok = (bool)okProp.GetValue(raw)!;
                var msg = t.GetProperty("ErrorMessage")?.GetValue(raw) as string;
                // Simulate Blocked drops ErrorMessage after parsing the module Failure; rebuild the printed phrase.
                if (!ok && msg is null && actionName is not null
                    && t.GetProperty("FailedGuards")?.GetValue(raw) is IEnumerable guards) {
                    var g = guards.Cast<object>().Select(x => x?.ToString()).FirstOrDefault(s => s is { Length: > 0 });
                    if (g is not null) msg = $"'{actionName}' blocked by policy '{g}'.";
                }
                var value = t.GetProperty("Value")?.GetValue(raw);
                if (ok && printed && value is not null) _print = value;
                return new(ok, ok ? null : msg, null,
                    Snap(printed ? null : _sim, printed ? _print : null, entity), StageOf(printed ? _print : _sim));
            }
            if (printed) _print = raw;
            return new(true, null, null, Snap(null, raw, entity), StageOf(raw));
        }
        catch (Exception ex) {
            var inner = ex is TargetInvocationException ti && ti.InnerException is not null ? ti.InnerException : ex;
            return new(false, inner.Message, inner.GetType().Name, new Dictionary<string, object?>(), null);
        }
    }

    Dictionary<string, object?> Snap(DomainEntityInstance? sim, object? print, Entity e) {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (sim is not null) {
            foreach (var p in e.Properties) d[p.Name] = Norm(sim.GetProperty<object>(p.Name));
            foreach (var rel in e.Navigations) d[rel.Name] = _store.GetLinkedTargets(rel.Name, sim).Count;
        }
        else if (print is not null) {
            foreach (var p in e.Properties) d[p.Name] = Norm(Prop(print, p.Name)?.GetValue(print));
            foreach (var rel in e.Navigations) {
                var v = Prop(print, rel.Name)?.GetValue(print);
                d[rel.Name] = v is ICollection c ? c.Count
                    : v is IEnumerable en && v is not string ? en.Cast<object>().Count() : v is null ? 0 : 1;
            }
        }
        return d;
    }

    static string? StageOf(object? inst) => inst is DomainEntityInstance dei ? dei.CurrentStage
        : Prop(inst, "CurrentStage")?.GetValue(inst)?.ToString();
    static PropertyInfo? Prop(object? inst, string name) => inst?.GetType().GetProperty(name)
        ?? inst?.GetType().GetProperties().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    static object? Norm(object? v) => v is null or bool or string ? v
        : v.GetType().IsEnum ? v.ToString()
        : v is IConvertible and not DateTime ? Convert.ToDecimal(v) : v;
    static Relationship? Nav(Entity e, string name) =>
        e.Navigations.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
    static IEnumerable<ParityHandle> Handles(object? value) => value switch {
        ParityHandle h => [h], IEnumerable<ParityHandle> hs => hs, _ => []
    };

    static object? PrintArg(object? v) {
        if (v is ParityHandle h) return h.Print;
        if (v is not IEnumerable<ParityHandle> hs) return v;
        var items = hs.ToList();
        if (items.Count == 0) return null;
        var arr = Array.CreateInstance(items[0].Print.GetType(), items.Count);
        for (var i = 0; i < items.Count; i++) arr.SetValue(items[i].Print, i);
        return arr;
    }

    static object? Call(object inst, string name, (string Name, object? Value)[] args) {
        var method = inst.GetType().GetMethods()
            .Where(m => m.Name == name && m is { IsPublic: true, IsStatic: false })
            .OrderByDescending(m => m.GetParameters().Length).First();
        var ps = method.GetParameters();
        var call = new object?[ps.Length];
        for (var i = 0; i < ps.Length; i++) {
            var match = args.FirstOrDefault(a => string.Equals(a.Name, ps[i].Name, StringComparison.OrdinalIgnoreCase));
            call[i] = match.Name is not null ? PrintArg(match.Value) : ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
        }
        return method.Invoke(inst, call);
    }
}
