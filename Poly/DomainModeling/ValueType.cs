namespace Poly.DomainModeling;

/// <summary>
/// A value type or owned document structure. Value types do not have independent lifecycle or identity
/// in the same way <see cref="Entity"/> types do; they are typically owned by entities.
/// </summary>
public sealed record ValueType(
    string Name,
    IReadOnlyList<Property> Properties,
    IReadOnlyList<Constraint> Constraints
) : DomainType(Name, Properties, Constraints);