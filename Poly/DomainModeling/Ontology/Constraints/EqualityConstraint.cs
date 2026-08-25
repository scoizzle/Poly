namespace Poly.DomainModeling.Ontology.Constraints;

public sealed record EqualityConstraint(object ExpectedValue) : Constraint;