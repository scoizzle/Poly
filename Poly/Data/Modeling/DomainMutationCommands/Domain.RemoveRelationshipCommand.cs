namespace Poly.Data.Modeling;

public sealed partial record Domain {
    internal sealed record RemoveRelationshipCommand(Domain Target, Relationship Relationship) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() {
            _index = DomainMutationCollection.RemoveAt(Target._objects, Relationship);
        }
        public override void Rollback() {
            DomainMutationCollection.Restore(Target._objects, Relationship, _index);
        }
        public override IEnumerable<Node> AffectedNodes => [Target, Relationship, Relationship.Source, Relationship.Target];
    }
}