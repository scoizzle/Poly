using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed class PublishEvent : Effect {
    private readonly Dictionary<string, IDomainValue> _propertyBindings = new(StringComparer.Ordinal);

    public required Event Event { get; init; }

    public IReadOnlyDictionary<string, IDomainValue> PropertyBindings => _propertyBindings;

    // Validation is now performed by EffectBindingAnalyzer only.

    public void BindProperty(Property eventProperty, IDomainValue value) {
        ArgumentNullException.ThrowIfNull(eventProperty);
        ArgumentNullException.ThrowIfNull(value);

        if (!ReferenceEquals(eventProperty.Type, value.Type)) {
            throw new InvalidOperationException(
                $"Binding for event property '{eventProperty.Name}' requires type '{eventProperty.Type.Name}' but got '{value.Type.Name}'.");
        }

        if (!_propertyBindings.TryAdd(eventProperty.Name, value)) {
            throw new InvalidOperationException(
                $"Binding for event property '{eventProperty.Name}' already exists on event '{Event.Name}'.");
        }
    }

    internal bool HasBindingFor(Property eventProperty) => _propertyBindings.ContainsKey(eventProperty.Name);
}