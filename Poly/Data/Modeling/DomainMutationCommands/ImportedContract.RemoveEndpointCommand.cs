namespace Poly.Data.Modeling;

public sealed partial record ImportedContract {
    internal sealed record RemoveEndpointCommand(ImportedContract Contract, ContractEndpoint Endpoint) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Contract._endpoints, Endpoint);
        public override void Rollback() => DomainMutationCollection.Restore(Contract._endpoints, Endpoint, _index);
        public override IEnumerable<Node> AffectedNodes => [Contract, Endpoint];
    }
}