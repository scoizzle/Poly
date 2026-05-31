namespace Poly.DomainModeling;

/// <summary>
/// Events are defined as <see cref="DomainType"/>s and are referenced from entities via <see cref="Entity.Events"/>.
/// They carry data through properties and can be bound to using <see cref="DomainExpression"/> in effects.
/// </summary>
public sealed record Event(
    string Name,
    IReadOnlyList<Property> Properties,
    IReadOnlyList<Constraint> Constraints
) : DomainType(Name, Properties, Constraints);