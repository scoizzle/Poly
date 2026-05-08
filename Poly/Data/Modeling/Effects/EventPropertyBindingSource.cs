namespace Poly.Data.Modeling.Effects;

/// <summary>
/// Names the source of a value bound to an event property when a <see cref="PublishEvent"/> effect fires.
/// </summary>
public abstract record EventPropertyBindingSource {
    /// <summary>Bind from a parameter declared on the owning action.</summary>
    public sealed record ActionParameter(string ParameterName) : EventPropertyBindingSource;

    /// <summary>Bind from a property on the owning entity instance.</summary>
    public sealed record EntityProperty(string PropertyName) : EventPropertyBindingSource;
}