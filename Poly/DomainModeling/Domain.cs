namespace Poly.DomainModeling;

/// <summary>
/// A <see cref="Domain"/> aggregates all <see cref="DomainType"/> definitions (entities, value types, events, primitives)
/// and the <see cref="Relationship"/>s between them. It serves as the top-level container for the entire
/// domain model and is the primary input to analyzers and lowering.
/// </summary>
/// <remarks>
/// This type is immutable. All mutation happens through builders (during construction) or is performed
/// by analyzers which attach metadata rather than mutating the model itself.
/// </remarks>
public sealed record Domain(
    string Name,
    IReadOnlyList<DomainType> Types,
    IReadOnlyList<Relationship> Relationships
) : DomainMember(Name) {
    public IReadOnlyList<ImportedContract> ImportedContracts { get; init; } = [];
    public IReadOnlyList<ContractBinding> ContractBindings { get; init; } = [];
    public sealed override IEnumerable<Node?> Children => [.. Types, .. Relationships, .. ImportedContracts, .. ContractBindings];
}