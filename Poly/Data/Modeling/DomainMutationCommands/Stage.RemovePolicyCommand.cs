namespace Poly.Data.Modeling;

public sealed partial record Stage {
    internal sealed record RemovePolicyCommand(Stage Stage, Policy Policy) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Stage._policies, Policy);
        public override void Rollback() => DomainMutationCollection.Restore(Stage._policies, Policy, _index);
        public override IEnumerable<Node> AffectedNodes => [Stage, Policy];
    }
}