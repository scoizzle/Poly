namespace Poly.DomainModeling.Effects;

/// <summary>
/// Effect that publishes a domain event, supplying property values via <see cref="DomainExpression"/> bindings.
/// 
/// Used both for explicit action effects and for automatic publication on stage entry/exit.
/// </summary>
public sealed record PublishEventEffect(
    DomainTypeReference EventType,
    IReadOnlyList<PropertyBinding> PropertyBindings
) : Effect {
    public sealed override IEnumerable<Node?> Children => [EventType, .. PropertyBindings];
}