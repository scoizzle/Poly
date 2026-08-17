namespace Poly.DomainModeling.Ontology.Effects;

/// <summary>
/// Effect that transitions the current entity instance to a different <see cref="Stage"/>.
/// </summary>
public sealed record StageTransitionEffect(StageReference TargetStage) : Effect {
    public sealed override IEnumerable<Node?> Children => [TargetStage];
}