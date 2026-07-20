namespace Poly.DomainModeling.Constraints;

/// <summary>
/// A default value for a property, set at construction time.
/// The expression can be a literal constant or a runtime default
/// like <c>now</c>, <c>today</c>, or <c>guid</c>.
/// </summary>
public sealed record DefaultValueConstraint(DomainExpression Expression) : Constraint;