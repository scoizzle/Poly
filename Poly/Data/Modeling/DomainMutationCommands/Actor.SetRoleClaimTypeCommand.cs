namespace Poly.Data.Modeling;

public sealed partial record Actor {
    internal sealed record SetRoleClaimTypeCommand(Actor Actor, string? RoleClaimType) : DomainMutationCommand {
        private readonly string? _previous = Actor.RoleClaimType;

        public override void Apply() => Actor.RoleClaimType = RoleClaimType;
        public override void Rollback() => Actor.RoleClaimType = _previous;
        public override IEnumerable<Node> AffectedNodes => [Actor];
    }
}