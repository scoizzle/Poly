namespace Poly.Data.Modeling;

public sealed partial record ImportedContract {
    internal sealed record AddEndpointCommand(ImportedContract Contract, ContractEndpoint Endpoint) : DomainMutationCommand {
        public override void Apply() => Contract._endpoints.Add(Endpoint);
        public override void Rollback() => Contract._endpoints.Remove(Endpoint);
        public override IEnumerable<Node> AffectedNodes => [Contract, Endpoint];
    }
}