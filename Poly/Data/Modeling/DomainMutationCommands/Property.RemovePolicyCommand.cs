namespace Poly.Data.Modeling;

public sealed partial record Property {
    internal sealed record RemovePolicyCommand(Property Property, Policy Policy) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Property._policies, Policy);
        public override void Rollback() => DomainMutationCollection.Restore(Property._policies, Policy, _index);
        public override IEnumerable<Node> AffectedNodes => [Property, Policy];
    }
}