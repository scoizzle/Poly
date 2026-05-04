using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

/// <summary>
/// A rule that targets a single property value with a set of constraints.
/// </summary>
public sealed record PropertyRule(Domain Domain, string Name, DomainValue Value, Constraint Constraints) : Rule(Domain, Name);

/// <summary>
/// A rule that expresses a comparison between two property values on the same domain object.
/// </summary>
public sealed record CrossPropertyRule(Domain Domain, string Name, DomainValue Left, DomainValue Right, DomainComparisonOperator Operator) : Rule(Domain, Name);