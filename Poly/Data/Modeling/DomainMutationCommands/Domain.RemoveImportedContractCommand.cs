namespace Poly.Data.Modeling;

public sealed partial record Domain {
    internal sealed record RemoveImportedContractCommand(Domain Target, ImportedContract Contract) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Target._objects, Contract);
        public override void Rollback() => DomainMutationCollection.Restore(Target._objects, Contract, _index);
        public override IEnumerable<Node> AffectedNodes => [Target, Contract];
    }
}