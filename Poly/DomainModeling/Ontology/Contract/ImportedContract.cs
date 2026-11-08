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

    /// <summary>
    /// Public entity names on this surface (decision 13). A name-only entry: no properties,
    /// stages, or mutable identity handle. Not a node, so not in <see cref="Children"/>.
    /// </summary>
    public IReadOnlyList<string> Entities { get; init; } = [];

    /// <summary>
    /// Enums named by the public surface. Enums carry no visibility mark; they are exported
    /// when a public signature (or an exported value type) reaches them, so a consumer can
    /// resolve the payload. Not a node, so not in <see cref="Children"/>.
    /// </summary>
    public IReadOnlyList<EnumType> Enums { get; init; } = [];

    /// <summary>
    /// SHA-256 of the surface and only the surface (spike #413 §3): endpoints, payloads,
    /// public entity names, enum members, public property constraints, public action
    /// preconditions, and the reserved <c>accepts</c> component. Computed, never stored, so
    /// the pin cannot drift from the contract it describes.
    /// </summary>
    public string Fingerprint => ContractSurfaceFingerprint.Compute(this);

    public sealed override IEnumerable<Node?> Children => [.. Types, .. Endpoints];
}
