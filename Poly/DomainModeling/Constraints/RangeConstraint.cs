namespace Poly.DomainModeling.Constraints;

public sealed record RangeConstraint(object? Minimum, object? Maximum) : Constraint;