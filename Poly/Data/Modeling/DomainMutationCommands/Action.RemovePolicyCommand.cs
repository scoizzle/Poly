namespace Poly.Data.Modeling;

public sealed partial record Action {
    internal sealed record RemovePolicyCommand(Action Action, Policy Policy) : DomainMutationCommand {
        public override void Apply() => Action._policies.Remove(Policy);
        public override void Rollback() => Action._policies.Add(Policy);
        public override IEnumerable<Node> AffectedNodes => [Action, Policy];
    }
}