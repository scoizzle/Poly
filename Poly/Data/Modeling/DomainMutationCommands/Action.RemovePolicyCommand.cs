namespace Poly.Data.Modeling;

public sealed partial record Action {
    internal sealed record RemovePolicyCommand(Action Action, Policy Policy) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Action._policies, Policy);
        public override void Rollback() => DomainMutationCollection.Restore(Action._policies, Policy, _index);
        public override IEnumerable<Node> AffectedNodes => [Action, Policy];
    }
}