namespace Poly.DomainModeling.Effects;

public sealed record ConditionalEffect(
    DomainExpression Condition,
    IReadOnlyList<Effect> ThenEffects,
    IReadOnlyList<Effect>? ElseEffects
) : Effect {
    public sealed override IEnumerable<Node?> Children => [Condition, .. ThenEffects, .. ElseEffects ?? []];
}