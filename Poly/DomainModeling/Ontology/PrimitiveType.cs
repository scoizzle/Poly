namespace Poly.DomainModeling.Ontology;

/// <summary>
/// Represents a primitive (built-in) type in the domain model (e.g. Text, Timestamp, Integer).
/// </summary>
public sealed record PrimitiveType(
    string Name,
    TypeCategory TypeCategory,
    IReadOnlyList<Constraint> Constraints
) : DomainType(Name, [], Constraints);