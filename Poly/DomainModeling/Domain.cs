namespace Poly.DomainModeling;

/// <summary>
/// A <see cref="Domain"/> aggregates all <see cref="DomainType"/> definitions (entities, value types, events, primitives)
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
    public sealed override IEnumerable<Node?> Children => [.. Types, .. ImportedContracts, .. ContractBindings];
}