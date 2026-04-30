using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed class CreateEntityInstance : Effect {
    public required Entity EntityType { get; init; }
    public Stage? InitialStage { get; init; }

    // Validation is now performed by EffectBindingAnalyzer only.
}