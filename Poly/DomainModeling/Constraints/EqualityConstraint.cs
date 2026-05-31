namespace Poly.DomainModeling.Constraints;

public sealed record EqualityConstraint(object ExpectedValue) : Constraint;