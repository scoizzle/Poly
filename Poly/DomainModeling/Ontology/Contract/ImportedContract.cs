namespace Poly.DomainModeling.Ontology.Contract;

public sealed record ImportedContract(
    string Name,
    ContractSourceKind SourceKind,
    string SourceIdentifier,
    string Version,
    IReadOnlyList<ContractEndpoint> Endpoints
) : DomainMember(Name) {
    /// <summary>ACL value types owned by this used sub-domain.</summary>
    public IReadOnlyList<ValueType> Types { get; init; } = [];

    public sealed override IEnumerable<Node?> Children => [.. Types, .. Endpoints];
}