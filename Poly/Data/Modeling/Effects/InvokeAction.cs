using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed record InvokeAction(Domain Domain) : Effect(Domain) {
    private readonly Dictionary<string, DomainValue> _parameterBindings = new(StringComparer.Ordinal);

    public required Action TargetAction { get; init; }

    public IReadOnlyDictionary<string, DomainValue> ParameterBindings => _parameterBindings;

    public void BindParameter(Property targetParameter, DomainValue value) {
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