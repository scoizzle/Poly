namespace Poly.Data.Modeling;

public sealed partial record EventSubscription {
    internal sealed record SetAudienceCommand(EventSubscription Subscription, EventSubscriptionAudience Audience, EventSubscriptionAudience PreviousAudience) : DomainMutationCommand {
        public override void Apply() => Subscription._routingMode = Audience is EventSubscriptionAudience.Correlated
            ? EventSubscriptionRoutingMode.Correlated
            : EventSubscriptionRoutingMode.Broadcast;

        public override void Rollback() => Subscription._routingMode = PreviousAudience is EventSubscriptionAudience.Correlated
            ? EventSubscriptionRoutingMode.Correlated
            : EventSubscriptionRoutingMode.Broadcast;

        public override IEnumerable<Node> AffectedNodes => [Subscription, Subscription.ConsumerEntity, Subscription.HandlerAction];
    }
}