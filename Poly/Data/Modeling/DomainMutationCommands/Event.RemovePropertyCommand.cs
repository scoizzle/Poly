namespace Poly.Data.Modeling;

public sealed partial record Event {
    internal sealed record RemovePropertyCommand(Event Event, Property Property) : DomainMutationCommand {
        public override void Apply() => Event._properties.Remove(Property);
        public override void Rollback() => Event._properties.Add(Property);
        public override IEnumerable<Node> AffectedNodes => [Event, Property];
    }
}