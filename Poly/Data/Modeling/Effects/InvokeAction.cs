using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed class InvokeAction : Effect {
    private readonly Dictionary<string, IDomainValue> _parameterBindings = new(StringComparer.Ordinal);

    public required Action TargetAction { get; init; }

    public IReadOnlyDictionary<string, IDomainValue> ParameterBindings => _parameterBindings;

    // Validation is now performed by EffectBindingAnalyzer only.

    public void BindParameter(Property targetParameter, IDomainValue value) {
        ArgumentNullException.ThrowIfNull(targetParameter);
        ArgumentNullException.ThrowIfNull(value);

        if (!TargetAction.Parameters.OfType<Property>().Any(p => string.Equals(p.Name, targetParameter.Name, StringComparison.Ordinal))) {
            throw new InvalidOperationException(
                $"Parameter '{targetParameter.Name}' does not exist on action '{TargetAction.Name}'.");
        }

        if (!ReferenceEquals(targetParameter.Type, value.Type)) {
            throw new InvalidOperationException(
                $"Binding for parameter '{targetParameter.Name}' requires type '{targetParameter.Type.Name}' but got '{value.Type.Name}'.");
        }

        if (!_parameterBindings.TryAdd(targetParameter.Name, value)) {
            throw new InvalidOperationException(
                $"Binding for parameter '{targetParameter.Name}' already exists on action '{TargetAction.Name}'.");
        }
    }

    internal bool HasBindingFor(Property targetParameter) => _parameterBindings.ContainsKey(targetParameter.Name);
}