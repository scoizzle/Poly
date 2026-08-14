using Poly.DomainModeling.Packs;

namespace Poly.DomainModeling;

/// <summary>
/// A <see cref="Domain"/> aggregates all <see cref="DomainType"/> definitions (entities, value types, primitives, enum types)
/// and the relationships between them. It serves as the top-level container for the entire
/// domain model and is the primary input to analyzers and lowering.
/// </summary>
/// <remarks>
/// Relationships are owned by their source entity (<c>Entity.Navigations</c>). The
/// domain-global <see cref="Relationships"/> view is a computed flatten of those navs —
/// it is never stored and carries no naming invariant of its own. The semantic
/// relationship view (source-scoped index, contracts, topology) is synthesized by the
/// analysis pipeline from entity navs.
/// This type is immutable. All mutation happens through builders (during construction) or is performed
/// by analyzers which attach metadata rather than mutating the model itself.
/// </remarks>
public sealed record Domain(
    string Name,
    IReadOnlyList<DomainType> Types
) : DomainMember(Name) {
    /// <summary>
    /// Derived flatten of every entity's navigation properties. Never stored; computed
    /// on demand so consumers without analysis (printer, queries, export fallbacks)
    /// can read the relationship view without depending on the pipeline.
    /// </summary>
    public IReadOnlyList<Relationship> Relationships =>
        Types.OfType<Entity>().SelectMany(e => e.Navigations).ToList();

    public IReadOnlyList<ImportedContract> ImportedContracts { get; init; } = [];
    public IReadOnlyList<ContractBinding> ContractBindings { get; init; } = [];

    /// <summary>
    /// Extension ids this compilation unit depends on (e.g. <c>temporal</c>).
    /// Ordered, unique, ordinal. Resolve via <see cref="ExtensionCatalog"/>.
    /// Another Poly domain is <see cref="ImportedContracts"/>, not this list.
    /// </summary>
    public IReadOnlyList<string> Extensions { get; init; } = [];

    public sealed override IEnumerable<Node?> Children => [.. Types, .. ImportedContracts, .. ContractBindings];

    /// <summary>Resolves this unit's extensions into parse/print/analysis tables.</summary>
    public DomainHost ResolveHost(ExtensionCatalog? catalog = null, bool failOnUnknown = false) =>
        (catalog ?? ExtensionCatalog.Core).ResolveHost(Extensions, failOnUnknown);
}