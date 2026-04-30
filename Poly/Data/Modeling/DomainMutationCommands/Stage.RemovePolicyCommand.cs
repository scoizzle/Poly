namespace Poly.Data.Modeling;

public sealed partial record Stage {
    internal sealed record RemovePolicyCommand(Stage Stage, Policy Policy) : DomainMutationCommand {
        public override void Apply() => Stage._policies.Remove(Policy);
        public override void Rollback() => Stage._policies.Add(Policy);
        public override IEnumerable<Node> AffectedNodes => [Stage, Policy];
    }
}