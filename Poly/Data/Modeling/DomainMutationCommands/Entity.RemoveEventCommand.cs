namespace Poly.Data.Modeling;

public partial record Entity {
    internal sealed record RemoveEventCommand(Entity Entity, Event Event) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Entity._events, Event);
        public override void Rollback() => DomainMutationCollection.Restore(Entity._events, Event, _index);
        public override IEnumerable<Node> AffectedNodes => [Entity, Event];
    }
}