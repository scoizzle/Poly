namespace Poly.Data.Modeling;

public sealed partial record Actor {
    internal sealed record AddClaimMappingCommand(Actor Actor, ActorClaimMapping Mapping) : DomainMutationCommand {
        public override void Apply() => Actor._claimMappings.Add(Mapping);
        public override void Rollback() => Actor._claimMappings.Remove(Mapping);
        public override IEnumerable<Node> AffectedNodes => [Actor];
    }
}