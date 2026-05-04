using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

public enum LogicalOperator { And, Or }

/// <summary>
/// A rule that requires the evaluating actor to be an instance of a specific actor type.
/// </summary>
public sealed record ActorTypeRule(Domain Domain, string Name, Actor ActorType) : Rule(Domain, Name);

/// <summary>
/// A rule that requires the evaluating actor to have a specific role claim value.
/// </summary>
public sealed record ActorRoleRule(Domain Domain, string Name, string Role) : Rule(Domain, Name);

/// <summary>
/// A rule that evaluates a constraint against a property on the evaluating actor.
/// Follows the same constraint model as <see cref="PropertyRule"/>.
/// </summary>
public sealed record ActorPropertyRule(Domain Domain, string Name, DomainValue ActorProperty, Constraint Constraints) : Rule(Domain, Name);

/// <summary>
/// Composes two rules with a logical operator (And / Or), enabling "this or that actor type" and similar constructs.
/// </summary>
public sealed record CompositeRule(Domain Domain, string Name, Rule Left, Rule Right, LogicalOperator Operator) : Rule(Domain, Name);