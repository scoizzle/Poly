namespace Poly.Data.Modeling;

public sealed partial record Actor {
    internal sealed record RemoveClaimMappingCommand(Actor Actor, ActorClaimMapping Mapping) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Actor._claimMappings, Mapping);
        public override void Rollback() => DomainMutationCollection.Restore(Actor._claimMappings, Mapping, _index);
        public override IEnumerable<Node> AffectedNodes => [Actor];
    }
}