namespace Poly.DomainModeling;

public sealed record EventSubscription(
    DomainTypeReference EventType,
    string HandlerActionName,
    string EventParameterName,
    EventSubscriptionRoutingMode RoutingMode,
    IReadOnlyList<EventCorrelationBinding> Correlations
) : DomainObject {
    public EventSubscription(DomainTypeReference eventType, string handlerActionName, string eventParameterName)
        : this(eventType, handlerActionName, eventParameterName, EventSubscriptionRoutingMode.Broadcast, []) { }

    public sealed override IEnumerable<Node?> Children => [EventType, .. Correlations];
}