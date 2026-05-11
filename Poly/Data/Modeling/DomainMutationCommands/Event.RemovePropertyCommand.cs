namespace Poly.Data.Modeling;

public sealed partial record Event {
    internal sealed record RemovePropertyCommand(Event Event, Property Property) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Event._properties, Property);
        public override void Rollback() => DomainMutationCollection.Restore(Event._properties, Property, _index);
        public override IEnumerable<Node> AffectedNodes => [Event, Property];
    }
}