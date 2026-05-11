namespace Poly.Data.Modeling;

public partial record Entity {
    internal sealed record RemovePropertyCommand(Entity Entity, Property Property) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Entity._properties, Property);
        public override void Rollback() => DomainMutationCollection.Restore(Entity._properties, Property, _index);
        public override IEnumerable<Node> AffectedNodes => [Entity, Property];
    }

}