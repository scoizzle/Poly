namespace Poly.DomainModeling.Ontology.Constraints;

public sealed record RangeConstraint(object? Minimum, object? Maximum) : Constraint;