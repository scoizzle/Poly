namespace Poly.DomainModeling.Ontology.Effects;

public sealed record AssignEffect(
    DomainExpression Target,
    DomainExpression Value
) : Effect {
    public sealed override IEnumerable<Node?> Children => [Target, Value];
}