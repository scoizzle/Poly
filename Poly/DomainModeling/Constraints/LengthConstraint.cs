namespace Poly.DomainModeling.Constraints;

public sealed record LengthConstraint(int MinLength, int MaxLength) : Constraint;