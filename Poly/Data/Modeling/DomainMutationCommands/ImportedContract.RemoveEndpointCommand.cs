namespace Poly.Data.Modeling;

public sealed partial record ImportedContract {
    internal sealed record RemoveEndpointCommand(ImportedContract Contract, ContractEndpoint Endpoint) : DomainMutationCommand {
        public override void Apply() => Contract._endpoints.Remove(Endpoint);
        public override void Rollback() => Contract._endpoints.Add(Endpoint);
        public override IEnumerable<Node> AffectedNodes => [Contract, Endpoint];
    }
}