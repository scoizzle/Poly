namespace Poly.DomainModeling.Effects;

public sealed record LinkRelationshipEffect(
    string RelationshipName,
    DomainExpression Target
) : Effect {
    public sealed override IEnumerable<Node?> Children => [Target];
}