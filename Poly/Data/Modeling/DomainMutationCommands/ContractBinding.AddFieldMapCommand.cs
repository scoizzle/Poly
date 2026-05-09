namespace Poly.Data.Modeling;

public sealed partial record ContractBinding {
    internal sealed record AddFieldMapCommand(ContractBinding Binding, ContractFieldMap FieldMap) : DomainMutationCommand {
        public override void Apply() => Binding._fieldMaps.Add(FieldMap);
        public override void Rollback() => Binding._fieldMaps.Remove(FieldMap);
        public override IEnumerable<Node> AffectedNodes => [Binding, FieldMap];
    }
}