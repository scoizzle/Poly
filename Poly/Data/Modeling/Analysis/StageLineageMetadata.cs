namespace Poly.Data.Modeling;

public sealed record StageLineageMetadata : IAnalysisMetadata {
    public int Depth { get; init; }
    public IReadOnlyList<Stage> Ancestors { get; init; } = [];
}