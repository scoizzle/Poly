namespace Poly.Data.Modeling;

public partial record Entity {
    internal sealed record AddRelationshipRefCommand(Entity Entity, Relationship Relationship) : DomainMutationCommand {
        public override void Apply() => Entity._relationships.Add(Relationship);
        public override void Rollback() => Entity._relationships.Remove(Relationship);
        public override IEnumerable<Node> AffectedNodes => [Entity, Relationship];
    }

}