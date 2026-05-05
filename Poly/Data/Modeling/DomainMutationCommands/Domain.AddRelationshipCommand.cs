namespace Poly.Data.Modeling;

public sealed partial record Domain {

    internal sealed record AddRelationshipCommand(Domain Target, Relationship Relationship) : DomainMutationCommand {
        public override void Apply() {
            Target._objects.Add(Relationship);
        }
        public override void Rollback() {
            Target._objects.Remove(Relationship);
        }
        public override IEnumerable<Node> AffectedNodes => [Target, Relationship, Relationship.Source, Relationship.Target];
    }
}