namespace Poly.Data.Modeling;

public sealed partial record EventSubscription {
    internal sealed record AddCorrelationBindingCommand(EventSubscription Subscription, EventCorrelationBinding Binding) : DomainMutationCommand {
        public override void Apply() => Subscription._correlations.Add(Binding);

        public override void Rollback() => Subscription._correlations.Remove(Binding);

        public override IEnumerable<Node> AffectedNodes => [Subscription, Subscription.ConsumerEntity, Subscription.HandlerAction, Binding];
    }
}