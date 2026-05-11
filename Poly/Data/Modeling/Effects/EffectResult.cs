using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

/// <summary>
/// Declares what an effect produces — like a C# function return type (named tuple).
/// This models the output contract, not execution.
/// </summary>
public sealed class EffectResult {
    private readonly Dictionary<string, DomainType> _produces = new(StringComparer.Ordinal);

    /// <summary>
    /// The named outputs this effect can produce.
    /// </summary>
    public IReadOnlyDictionary<string, DomainType> Outputs => _produces.AsReadOnly();

    /// <summary>
    /// Declare that this effect produces a named value of the given type.
    /// </summary>
    internal DomainType? SetOutput(string name, DomainType type) {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(type);

        _produces.TryGetValue(name, out var previous);
        _produces[name] = type;
        return previous;
    }

    internal void RestoreOutput(string name, DomainType? previous) {
        ArgumentNullException.ThrowIfNull(name);

        if (previous is null) {
            _produces.Remove(name);
            return;
        }

        _produces[name] = previous;
    }

    /// <summary>
    /// Check if this effect produces a specific named output.
    /// </summary>
    public bool HasOutput(string name) => _produces.ContainsKey(name);
}