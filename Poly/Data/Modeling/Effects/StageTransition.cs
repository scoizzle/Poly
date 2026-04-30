using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed class StageTransition : Effect {
    public required Stage TargetStage { get; init; }

    // Validation is now performed by EffectBindingAnalyzer only.
}