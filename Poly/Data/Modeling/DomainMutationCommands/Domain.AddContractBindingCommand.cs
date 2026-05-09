namespace Poly.Data.Modeling;

public sealed partial record Domain {
    internal sealed record AddContractBindingCommand(Domain Target, ContractBinding Binding) : DomainMutationCommand {
        public override void Apply() => Target._objects.Add(Binding);
        public override void Rollback() => Target._objects.Remove(Binding);
        public override IEnumerable<Node> AffectedNodes => [Target, Binding];
    }
}