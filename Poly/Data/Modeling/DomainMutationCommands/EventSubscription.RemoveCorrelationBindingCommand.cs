namespace Poly.Data.Modeling;

public sealed partial record EventSubscription {
    internal sealed record RemoveCorrelationBindingCommand(EventSubscription Subscription, EventCorrelationBinding Binding) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Subscription._correlations, Binding);

        public override void Rollback() => DomainMutationCollection.Restore(Subscription._correlations, Binding, _index);

        public override IEnumerable<Node> AffectedNodes => [Subscription, Subscription.ConsumerEntity, Subscription.HandlerAction, Binding];
    }
}