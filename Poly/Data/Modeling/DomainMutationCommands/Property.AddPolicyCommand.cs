namespace Poly.Data.Modeling;

public sealed partial record Property {
    internal sealed record AddPolicyCommand(Property Property, Policy Policy) : DomainMutationCommand {
        public override void Apply() => Property._policies.Add(Policy);
        public override void Rollback() => Property._policies.Remove(Policy);
        public override IEnumerable<Node> AffectedNodes => [Property, Policy];
    }
}