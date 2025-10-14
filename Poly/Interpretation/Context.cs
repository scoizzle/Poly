using System.Collections.Frozen;

namespace Poly.Interpretation;

public class Context {
    private readonly FrozenDictionary<string, Parameter> _parameters;

    public Context(params IEnumerable<Parameter> parameters) {
        ArgumentNullException.ThrowIfNull(parameters);

        _parameters = parameters.ToFrozenDictionary(p => p.Name);
    }

    public IEnumerable<Parameter> Parameters => _parameters.Values;
    public Parameter GetParameter(string name) => _parameters.TryGetValue(name, out var parameter) ? parameter : throw new KeyNotFoundException($"Parameter '{name}' not found.");
}
