using Poly.DomainModeling.Effects;

namespace Poly.DomainModeling;

/// <summary>
/// Small builder for configuring a CreateEntityInstance effect.
/// </summary>
public sealed class CreateEffectBuilder {
    private readonly string _typeName;
    private readonly List<PropertyBinding> _initializers = new();

    internal CreateEffectBuilder(string typeName) {
        _typeName = Guard.ThrowIfNullOrEmpty(typeName);
    }

    /// <summary>
    /// Sets a property on the created instance using a DomainExpression (e.g. Parameter("X") or Owned(...) ).
    /// </summary>
    public CreateEffectBuilder Set(string propertyName, DomainExpression expression) {
        _initializers.Add(new PropertyBinding(
            Guard.ThrowIfNullOrEmpty(propertyName),
            expression
        ));
        return this;
    }

    internal CreateEntityInstance Build() {
        return new CreateEntityInstance(
            new DomainTypeReference(_typeName),
            _initializers
        );
    }
}