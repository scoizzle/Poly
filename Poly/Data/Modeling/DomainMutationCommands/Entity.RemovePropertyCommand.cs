namespace Poly.Data.Modeling;

public partial record Entity {
    internal sealed record RemovePropertyCommand(Entity Entity, Property Property) : DomainMutationCommand {
        public override void Apply() => Entity._properties.Remove(Property);
        public override void Rollback() => Entity._properties.Add(Property);
        public override IEnumerable<Node> AffectedNodes => [Entity, Property];
    }

}