namespace Poly.DomainModeling.Ontology.Constraints;

public sealed record LengthConstraint(int MinLength, int MaxLength) : Constraint;