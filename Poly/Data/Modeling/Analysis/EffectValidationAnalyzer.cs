namespace Poly.Data.Modeling.Analysis;

public sealed class EffectValidationAnalyzer : IDomainModelAnalyzer {
    public string Name => nameof(EffectValidationAnalyzer);

    public void Analyze(Domain domain, DomainModelAnalysisContext context) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var entity in domain.GetAvailableEntities().Where(candidate => candidate is not Relationship)) {
            foreach (var action in entity.Actions) {
                foreach (var effect in action.Effects) {
                    try {
                        effect.Validate(entity);
                    }
                    catch (Exception ex) {
                        context.ReportError(
                            code: "DM0003",
                            message: $"Effect validation failed for action '{action.Name}' on entity '{entity.Name}': {ex.Message}",
                            location: $"Entity:{entity.Name}/Action:{action.Name}");
                    }
                }
            }
        }
    }
}