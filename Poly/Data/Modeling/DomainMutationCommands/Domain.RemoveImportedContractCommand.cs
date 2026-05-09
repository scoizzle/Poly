namespace Poly.Data.Modeling;

public sealed partial record Domain {
    internal sealed record RemoveImportedContractCommand(Domain Target, ImportedContract Contract) : DomainMutationCommand {
        public override void Apply() => Target._objects.Remove(Contract);
        public override void Rollback() => Target._objects.Add(Contract);
        public override IEnumerable<Node> AffectedNodes => [Target, Contract];
    }
}