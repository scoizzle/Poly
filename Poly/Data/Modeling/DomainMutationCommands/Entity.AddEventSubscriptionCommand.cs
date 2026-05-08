namespace Poly.Data.Modeling;

public partial record Entity {
    internal sealed record AddEventSubscriptionCommand(Entity Entity, EventSubscription Subscription) : DomainMutationCommand {
        public override void Apply() => Entity._eventSubscriptions.Add(Subscription);

        public override void Rollback() => Entity._eventSubscriptions.Remove(Subscription);

        public override IEnumerable<Node> AffectedNodes => [Entity, Subscription, Subscription.HandlerAction];
    }
}