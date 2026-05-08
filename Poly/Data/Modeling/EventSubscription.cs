using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed partial record EventSubscription : DomainMember {
    internal readonly List<EventCorrelationBinding> _correlations = [];
    internal EventSubscriptionAudience _audience = EventSubscriptionAudience.Default;

    public EventSubscription(Domain domain, Entity consumerEntity, Event eventType, Action handlerAction)
        : base(domain, $"{handlerAction.Name}<-{eventType.Name}") {
        ArgumentNullException.ThrowIfNull(consumerEntity);
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(handlerAction);

        ConsumerEntity = consumerEntity;
        EventType = eventType;
        HandlerAction = handlerAction;
    }

    public Entity ConsumerEntity { get; }

    public Event EventType { get; }

    public Action HandlerAction { get; }

    public EventSubscriptionAudience Audience => _audience;

    public IReadOnlyCollection<EventCorrelationBinding> Correlations => _correlations.AsReadOnly();

    public override IEnumerable<DomainObject> ChildObjects => _correlations;
}