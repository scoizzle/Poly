namespace Poly.DomainModeling.Ontology;

/// <summary>
/// A value type or owned document structure. Value types do not have independent lifecycle or identity
/// in the same way <see cref="Entity"/> types do; they are typically owned by entities.
/// </summary>
public sealed record ValueType(
    string Name,
    IReadOnlyList<Property> Properties,
    IReadOnlyList<Constraint> Constraints
) : DomainType(Name, Properties, Constraints) {
    /// <summary>
    /// Export visibility. <see cref="Visibility.Internal"/> unless marked <c>public</c>.
    /// </summary>
    public Visibility Visibility { get; init; } = Visibility.Internal;
}