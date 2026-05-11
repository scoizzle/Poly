namespace Poly.Data.Modeling;

public partial record Entity {
    internal sealed record RemovePolicyCommand(Entity Entity, Policy Policy) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Entity._policies, Policy);
        public override void Rollback() => DomainMutationCollection.Restore(Entity._policies, Policy, _index);
        public override IEnumerable<Node> AffectedNodes => [Entity, Policy];
    }
}