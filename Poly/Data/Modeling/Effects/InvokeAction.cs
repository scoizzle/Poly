namespace Poly.Data.Modeling.Effects;

public sealed class InvokeAction : Effect {
    public required Action TargetAction { get; init; }

    // TODO: Add support for action parameters
}