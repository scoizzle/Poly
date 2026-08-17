using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.ContractFill;

namespace Poly.DomainModeling;

/// <summary>
/// A <see cref="Domain"/> aggregates all <see cref="DomainType"/> definitions (entities, value types, primitives, enum types).
/// It serves as the top-level container for the entire domain model and is the primary
/// input to analyzers and lowering.
/// </summary>
/// <remarks>
/// Relationships are owned by their source entity (<c>Entity.Navigations</c>) and are
/// not a domain-level member. The relationship view (source-scoped index, contracts,
/// topology) is computed from the ontology by the analysis pipeline — reach it via
/// <c>analysis.GetRelationships(entity)</c> / <c>analysis.GetAllRelationships(domain)</c>.
/// This type is immutable. All mutation happens through builders (during construction) or is performed
/// by analyzers which attach metadata rather than mutating the model itself.
/// </remarks>
public sealed record Domain(
    string Name,
    IReadOnlyList<DomainType> Types
) : DomainMember(Name) {
    public IReadOnlyList<ImportedContract> ImportedContracts { get; init; } = [];
    public IReadOnlyList<ContractBinding> ContractBindings { get; init; } = [];

    /// <summary>
    /// Extension ids this compilation unit depends on (e.g. <c>temporal</c>).
    /// Ordered, unique, ordinal. Resolve via <see cref="ExtensionCatalog"/>.
    /// Another Poly domain is <see cref="ImportedContracts"/>, not this list.
    /// </summary>
    public IReadOnlyList<string> Extensions { get; init; } = [];

    public sealed override IEnumerable<Node?> Children => [.. Types, .. ImportedContracts, .. ContractBindings];
}