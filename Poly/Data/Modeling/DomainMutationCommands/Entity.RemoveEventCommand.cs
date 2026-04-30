namespace Poly.Data.Modeling;

public partial record Entity {
    internal sealed record RemoveEventCommand(Entity Entity, Event Event) : DomainMutationCommand {
        public override void Apply() => Entity._events.Remove(Event);
        public override void Rollback() => Entity._events.Add(Event);
        public override IEnumerable<Node> AffectedNodes => [Entity, Event];
    }
}