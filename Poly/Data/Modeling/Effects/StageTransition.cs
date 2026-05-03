using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

/// <summary>
/// Represents an effect that transitions an entity instance to a different stage in its lifecycle.
/// </summary>
/// <remarks>
/// For every action A on stage S, for every property P newly required by transitioning to S, there exists an effect E in A.Effects such that E produces a value for P.
/// This invariant is crucial for ensuring that all necessary data is available when an entity transitions to a new stage, and it is the responsibility of the EffectAnalyzer to validate this condition.
/// </remarks>
public sealed record StageTransition(Domain Domain) : Effect(Domain) {
    public required Stage TargetStage { get; init; }
}