using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed partial record EventSubscription : DomainMember {
    internal readonly List<EventCorrelationBinding> _correlations = [];
    internal EventSubscriptionRoutingMode _routingMode = EventSubscriptionRoutingMode.Broadcast;
    internal string _eventParameterName;

    public EventSubscription(Domain domain, Entity consumerEntity, Event eventType, Action handlerAction)
        : base(domain, $"{handlerAction.Name}<-{eventType.Name}") {
        ArgumentNullException.ThrowIfNull(consumerEntity);
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(handlerAction);

        ConsumerEntity = consumerEntity;
        EventType = eventType;
        HandlerAction = handlerAction;
        _eventParameterName = ResolveDefaultEventParameterName(handlerAction, eventType);
    }

    public EventSubscription(Domain domain, Entity consumerEntity, Event eventType, Action handlerAction, string eventParameterName)
        : this(domain, consumerEntity, eventType, handlerAction) {
        _eventParameterName = eventParameterName;
    }

    public Entity ConsumerEntity { get; }

    public Event EventType { get; }

    public Action HandlerAction { get; }

    public string EventParameterName => _eventParameterName;

    public EventSubscriptionRoutingMode RoutingMode => _routingMode;

    public EventSubscriptionAudience Audience => _routingMode switch {
        EventSubscriptionRoutingMode.Correlated => new EventSubscriptionAudience.Correlated(),
        _ => new EventSubscriptionAudience.Broadcast()
    };

    public IReadOnlyCollection<EventCorrelationBinding> Correlations => _correlations.AsReadOnly();

    public override IEnumerable<DomainObject> ChildObjects => _correlations;

    private static string ResolveDefaultEventParameterName(Action handlerAction, Event eventType) {
        var eventParameter = handlerAction.Parameters
            .OfType<Property>()
            .FirstOrDefault(parameter => DomainTypeAssignability.CanAssign(parameter.Type, eventType));

        return eventParameter?.Name ?? "event";
    }
}