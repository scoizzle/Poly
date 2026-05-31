using Poly.DomainModeling.Effects;

namespace Poly.DomainModeling;

/// <summary>
/// Small builder for configuring a PublishEventEffect.
/// </summary>
public sealed class PublishEventBuilder {
    private readonly string _eventName;
    private readonly List<PropertyBinding> _bindings = new();

    internal PublishEventBuilder(string eventName) {
        _eventName = Guard.ThrowIfNullOrEmpty(eventName);
    }

    public PublishEventBuilder Bind(string propertyName, DomainExpression expression) {
        _bindings.Add(new PropertyBinding(
            Guard.ThrowIfNullOrEmpty(propertyName),
            expression
        ));
        return this;
    }

    internal PublishEventEffect Build() {
        return new PublishEventEffect(
            new DomainTypeReference(_eventName),
            _bindings
        );
    }
}