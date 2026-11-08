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

    /// <summary>
    /// The <c>uses</c> id whose contribution seeded this contract, or <c>null</c> when a
    /// <c>contract</c> block in the body declared it. A seeded contract is not printed
    /// back as a block: the <c>uses</c> line is the export-package reference.
    /// </summary>
    public string? UsesId { get; init; }

    /// <summary>
    /// The surface version the source pinned on its <c>uses</c> line, or <c>null</c> when
    /// it wrote none. This is the caller's claim; <see cref="Version"/> is the package's.
    /// The pin is recorded, never folded into <see cref="Version"/>.
    /// </summary>
    public string? Pin { get; init; }

    public sealed override IEnumerable<Node?> Children => [.. Types, .. Endpoints];
}