namespace Poly.Data.Modeling;

public partial record Entity {
    internal sealed record RemoveEventSubscriptionCommand(Entity Entity, EventSubscription Subscription) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Entity._eventSubscriptions, Subscription);

        public override void Rollback() => DomainMutationCollection.Restore(Entity._eventSubscriptions, Subscription, _index);

        public override IEnumerable<Node> AffectedNodes => [Entity, Subscription, Subscription.HandlerAction];
    }
}