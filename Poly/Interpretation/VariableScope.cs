namespace Poly.Interpretation;

public sealed class VariableScope {
    public Dictionary<string, Variable> Variables { get; } = new();

    public Variable? GetVariable(string name) => Variables.TryGetValue(name, out var variable) ? variable : default;
    public Variable SetVariable(string name, Value value) {
        if (Variables.TryGetValue(name, out var variable)) {
            variable.Value = value;
            return variable;
        } else {
            variable = new Variable(name, value);
            Variables[name] = variable;
            return variable;
        }
    }
}