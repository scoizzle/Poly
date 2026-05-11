namespace Poly.Data.Modeling;

public partial record Entity {

    internal sealed record RemoveRelationshipRefCommand(Entity Entity, Relationship Relationship) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Entity._relationships, Relationship);
        public override void Rollback() => DomainMutationCollection.Restore(Entity._relationships, Relationship, _index);
        public override IEnumerable<Node> AffectedNodes => [Entity, Relationship];
    }

}