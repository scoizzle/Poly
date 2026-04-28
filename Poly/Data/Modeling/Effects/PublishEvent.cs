using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed class PublishEvent : Effect {
    private readonly Dictionary<string, IDomainValue> _propertyBindings = new(StringComparer.Ordinal);

    public required Event Event { get; init; }

    public IReadOnlyDictionary<string, IDomainValue> PropertyBindings => _propertyBindings;

    public override IReadOnlyCollection<IDomainValue> RequiredParameters => _propertyBindings.Values.ToArray();

    public override void Validate(Entity entity) {
        ArgumentNullException.ThrowIfNull(entity);
        Event.ThrowIfMismatchedDomain(entity.Domain);

        foreach (var eventProperty in Event.Properties) {
            if (!HasBindingFor(eventProperty)) {
                throw new InvalidOperationException(
                    $"PublishEvent for '{Event.Name}' is missing binding for event property '{eventProperty.Name}'.");
            }
        }
    }

    public void BindProperty(Property eventProperty, IDomainValue value) {
        ArgumentNullException.ThrowIfNull(eventProperty);
        ArgumentNullException.ThrowIfNull(value);

        eventProperty.ThrowIfMismatchedDomain(Event.Domain);
        value.ThrowIfMismatchedDomain(Event.Domain);

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