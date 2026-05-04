namespace Poly.Data.Modeling;

public sealed partial record Actor {
    internal sealed record RemoveClaimMappingCommand(Actor Actor, ActorClaimMapping Mapping) : DomainMutationCommand {
        public override void Apply() => Actor._claimMappings.Remove(Mapping);
        public override void Rollback() => Actor._claimMappings.Add(Mapping);
        public override IEnumerable<Node> AffectedNodes => [Actor];
    }
}