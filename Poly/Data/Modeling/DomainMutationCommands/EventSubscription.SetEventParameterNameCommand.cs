namespace Poly.Data.Modeling;

public sealed partial record EventSubscription {
    internal sealed record SetEventParameterNameCommand(
        EventSubscription Subscription,
        string EventParameterName,
        string PreviousEventParameterName) : DomainMutationCommand {

        public override void Apply() => Subscription._eventParameterName = EventParameterName;

        public override void Rollback() => Subscription._eventParameterName = PreviousEventParameterName;

        public override IEnumerable<Node> AffectedNodes => [Subscription, Subscription.ConsumerEntity, Subscription.HandlerAction];
    }
}