namespace Poly.Data.Modeling;

public partial record Entity {
    internal sealed record AddPolicyCommand(Entity Entity, Policy Policy) : DomainMutationCommand {
        public override void Apply() => Entity._policies.Add(Policy);
        public override void Rollback() => Entity._policies.Remove(Policy);
        public override IEnumerable<Node> AffectedNodes => [Entity, Policy];
    }

}