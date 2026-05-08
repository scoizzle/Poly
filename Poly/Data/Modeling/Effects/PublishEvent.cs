namespace Poly.Data.Modeling.Effects;

public sealed record PublishEvent(Domain Domain) : Effect(Domain) {
    internal readonly Dictionary<string, EventPropertyBindingSource> _bindings = new(StringComparer.Ordinal);

    public required Event Event { get; init; }

    /// <summary>Maps event property name → the source that provides its value at runtime.</summary>
    public IReadOnlyDictionary<string, EventPropertyBindingSource> PropertyBindings => _bindings;

    internal bool HasBindingFor(Property eventProperty) => _bindings.ContainsKey(eventProperty.Name);
}