using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed record PublishEvent(Domain Domain) : Effect(Domain) {
    private readonly Dictionary<string, DomainValue> _propertyBindings = new(StringComparer.Ordinal);

    public required Event Event { get; init; }

    public IReadOnlyDictionary<string, DomainValue> PropertyBindings => _propertyBindings;

    public void BindProperty(Property eventProperty, DomainValue value) {
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