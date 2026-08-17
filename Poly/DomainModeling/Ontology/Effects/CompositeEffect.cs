namespace Poly.DomainModeling.Ontology.Effects;

public sealed record CompositeEffect(
    IReadOnlyList<Effect> Effects
) : Effect {
    public sealed override IEnumerable<Node?> Children => [.. Effects];
}