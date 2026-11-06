namespace Poly.DomainModeling.Ontology;

/// <summary>
/// A first-class enum type with a fixed set of named members.
/// Properties reference an <see cref="EnumType"/> by name (not by string + constraint).
/// Expressions compare with bare identifiers: <c>Status is Active</c>.
/// </summary>
public sealed record EnumType(
    string Name,
    IReadOnlyList<string> MemberNames,
    IReadOnlyList<Constraint> Constraints
) : DomainType(Name, Properties: [], Constraints) {
    /// <summary>
    /// Metadata attached to individual members (term IRI, term name, label, description).
    /// Keyed by member name (ordinal); a member with no metadata has no entry. The member
    /// list itself stays <see cref="MemberNames"/>, so every existing member comparison and
    /// emission path is unchanged.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Facet>> MemberFacets { get; init; } =
        new Dictionary<string, IReadOnlyList<Facet>>(StringComparer.Ordinal);

    public override IEnumerable<Node?> Children =>
        [.. base.Children, .. MemberFacets.Values.SelectMany(facets => facets)];
}