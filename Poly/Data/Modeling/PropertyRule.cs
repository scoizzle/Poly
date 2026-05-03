namespace Poly.Data.Modeling;

/// <summary>
/// A rule that targets a single property value.
/// </summary>
/// <param name="Value"><inheritdoc/></param>
/// <param name="Constraints"><inheritdoc/></param>
public sealed record PropertyRule(Domain Domain, string Name, TypeSystem.DomainValue Value, Validation.Constraint Constraints) : Rule(Domain, Name, Value, Constraints);


/// <summary>
/// A policy rule backed by an expression factory, useful for cross-property predicates.
/// </summary>
/// <param name="Value"><inheritdoc/></param>
/// <param name="Constraints"><inheritdoc/></param>
/// <param name="Predicate"></param>
public sealed record PredicateRule(Domain Domain, string Name, TypeSystem.DomainValue Value, Validation.Constraint Constraints, Func<Node, Node> Predicate) : Rule(Domain, Name, Value, Constraints) {
    public Node ToInterpretationNode(Node subject) {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(Predicate);

        return Predicate(subject);
    }
}