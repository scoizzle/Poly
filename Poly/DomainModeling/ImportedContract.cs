namespace Poly.DomainModeling;

public sealed record ImportedContract(
    string Name,
    ContractSourceKind SourceKind,
    string SourceIdentifier,
    string Version,
    IReadOnlyList<ContractEndpoint> Endpoints
) : DomainMember(Name) {
    public sealed override IEnumerable<Node?> Children => [.. Endpoints];
}