namespace Poly.Data.Modeling;

public sealed partial record Domain {
    internal sealed record RemoveRelationshipCommand(Domain Target, Relationship Relationship) : DomainMutationCommand {
        public override void Apply() => Target._relationships.Remove(Relationship);
        public override void Rollback() => Target._relationships.Add(Relationship);
        public override IEnumerable<Node> AffectedNodes => [Target, Relationship, Relationship.Source, Relationship.Target];
    }
}