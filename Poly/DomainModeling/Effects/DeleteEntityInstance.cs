namespace Poly.DomainModeling.Effects;

public sealed record DeleteEntityInstance(
    DomainTypeReference EntityType
) : Effect {
    public sealed override IEnumerable<Node?> Children => [EntityType];
}