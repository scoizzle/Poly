namespace Poly.Data.Modeling.Effects;

public sealed class StageTransition : Effect {
    public required Stage TargetStage { get; init; }
}