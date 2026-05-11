namespace Poly.Data.Modeling;

public sealed partial record ContractBinding {
    internal sealed record RemoveFieldMapCommand(ContractBinding Binding, ContractFieldMap FieldMap) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Binding._fieldMaps, FieldMap);
        public override void Rollback() => DomainMutationCollection.Restore(Binding._fieldMaps, FieldMap, _index);
        public override IEnumerable<Node> AffectedNodes => [Binding, FieldMap];
    }
}