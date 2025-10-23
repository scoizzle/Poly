using System.Collections.Concurrent;

namespace Poly.Interpretation;

public sealed class VariableScope(VariableScope? parentScope = null) {
    public VariableScope? ParentScope { get; } = parentScope;

    public ConcurrentDictionary<string, Variable> Variables { get; private init; } = new();

    public Variable? GetVariable(string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Variables.TryGetValue(name, out var variable)
            ? variable
            : ParentScope?.GetVariable(name);
    }

    public Variable SetVariable(string name, Value? value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Variables.GetOrAdd(name, static (name, value) => new Variable(name, value), value);
    }

    public VariableScope Clone() {
        var clone = new VariableScope(ParentScope) {
            Variables = new ConcurrentDictionary<string, Variable>(Variables)
        };
        return clone;
    }
}