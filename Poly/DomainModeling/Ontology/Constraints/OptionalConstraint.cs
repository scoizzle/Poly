namespace Poly.DomainModeling.Ontology.Constraints;

/// <summary>
/// Marks a property whose value may be absent. Required is the default; <c>optional</c>
/// is the explicit opt-out. It is orthogonal to <see cref="DefaultValueConstraint"/>,
/// which supplies an initial value and makes a property omittable at create: a property
/// can be required-valued and still carry a default (omit at create, get the default), or
/// be optional and undefaulted (omit at create, value absent).
/// </summary>
public sealed record OptionalConstraint : Constraint;