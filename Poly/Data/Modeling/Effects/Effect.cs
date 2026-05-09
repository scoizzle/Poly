using System.Collections.Immutable;

using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

/// <summary>
/// Represents an effect that can occur as a result of an action. 
/// Effects can include publishing events, invoking external services, or modifying data.
/// </summary>
/// <remarks>
/// Effects are designed to be extensible, allowing for a wide range of behaviors to be implemented.
/// Effects declare what they produce via <see cref="Result"/> for downstream wiring.
/// </remarks>
public abstract record Effect(Domain Domain) : DomainObject(Domain) {
    private readonly EffectResult _result = new();

    protected Effect(Domain domain, Action<EffectResult> configureResults) : this(domain) {
        configureResults?.Invoke(_result);
    }

    /// <summary>
    /// Declares what this effect produces — like a function return type (named tuple).
    /// Use this to wire outputs to inputs of downstream effects.
    /// </summary>
    public EffectResult Result => _result;

    /// <summary>
    /// Convenience: declare that this effect produces a named output of the given type.
    /// </summary>
    public void Produces(string name, DomainType type) => _result.Produces(name, type);

    /// <summary>
    /// Shortcut: bind an output from this effect to a parameter on a target effect.
    /// At code generation time, this becomes: target.BindParameter(param, this.Output[name]).
    /// </summary>
    public void BindOutputTo(string outputName, Effect targetEffect, string targetParamName) {
        ArgumentNullException.ThrowIfNull(outputName);
        ArgumentNullException.ThrowIfNull(targetEffect);
        ArgumentNullException.ThrowIfNull(targetParamName);

        if (!_result.HasOutput(outputName)) {
            throw new InvalidOperationException(
                $"Effect '{GetType().Name}' does not produce output '{outputName}'.");
        }

        if (targetEffect._incomingBindings is null) {
            targetEffect._incomingBindings = new Dictionary<string, EffectValueRef>(StringComparer.Ordinal);
        }
        targetEffect._incomingBindings[targetParamName] = new EffectValueRef(GetType().Name, outputName);
    }

    public IReadOnlyDictionary<string, EffectValueRef> IncomingBindings =>
        _incomingBindings is not null
            ? _incomingBindings
            : ImmutableDictionary<string, EffectValueRef>.Empty;

    private Dictionary<string, EffectValueRef>? _incomingBindings;
}