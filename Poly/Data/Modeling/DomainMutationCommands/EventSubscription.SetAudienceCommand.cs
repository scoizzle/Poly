namespace Poly.Data.Modeling;

public sealed partial record EventSubscription {
    internal sealed record SetAudienceCommand(EventSubscription Subscription, EventSubscriptionAudience Audience, EventSubscriptionAudience PreviousAudience) : DomainMutationCommand {
        public override void Apply() => Subscription._audience = Audience;

        public override void Rollback() => Subscription._audience = PreviousAudience;

        public override IEnumerable<Node> AffectedNodes => [Subscription, Subscription.ConsumerEntity, Subscription.HandlerAction];
    }
}