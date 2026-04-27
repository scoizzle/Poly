namespace Poly.Data.Modeling;

public sealed record StageTransitionRequirementAnalysis(
    IReadOnlyCollection<Property> CurrentRequiredProperties,
    IReadOnlyCollection<Property> TargetRequiredProperties,
    IReadOnlyCollection<Property> NewlyRequiredProperties);

public static class StageTransitionRequirementAnalyzer {
    private static readonly DomainModelAnalyzer Analyzer = new();

    public static StageTransitionRequirementAnalysis Analyze(Stage currentStage, Stage targetStage, Entity entityType) {
        return Analyzer.AnalyzeStageTransitionRequirements(currentStage, targetStage, entityType);
    }
}