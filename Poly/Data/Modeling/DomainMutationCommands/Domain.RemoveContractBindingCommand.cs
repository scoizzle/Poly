namespace Poly.Data.Modeling;

public sealed partial record Domain {
    internal sealed record RemoveContractBindingCommand(Domain Target, ContractBinding Binding) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Target._objects, Binding);
        public override void Rollback() => DomainMutationCollection.Restore(Target._objects, Binding, _index);
        public override IEnumerable<Node> AffectedNodes => [Target, Binding];
    }
}