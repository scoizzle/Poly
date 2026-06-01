namespace Poly.DomainModeling.Effects;

public sealed record TransitionRelationshipEffect(
    string RelationshipName,
    StageReference TargetStage
) : Effect {
    public sealed override IEnumerable<Node?> Children => [TargetStage];
}