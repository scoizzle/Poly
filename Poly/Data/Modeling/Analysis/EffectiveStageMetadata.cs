namespace Poly.Data.Modeling;

public sealed record EffectiveStageMetadata : IAnalysisMetadata {
    public IReadOnlyCollection<Action> EffectiveActions { get; init; } = [];
    public IReadOnlyCollection<Policy> EffectivePolicies { get; init; } = [];
}