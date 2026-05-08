namespace Poly.Data.Modeling;

public sealed partial record EventSubscription {
    internal sealed record SetRoutingModeCommand(
        EventSubscription Subscription,
        EventSubscriptionRoutingMode RoutingMode,
        EventSubscriptionRoutingMode PreviousRoutingMode) : DomainMutationCommand {

        public override void Apply() => Subscription._routingMode = RoutingMode;

        public override void Rollback() => Subscription._routingMode = PreviousRoutingMode;

        public override IEnumerable<Node> AffectedNodes => [Subscription, Subscription.ConsumerEntity, Subscription.HandlerAction];
    }
}