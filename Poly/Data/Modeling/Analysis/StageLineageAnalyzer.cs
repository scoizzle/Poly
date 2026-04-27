namespace Poly.Data.Modeling.Analysis;

public sealed class StageLineageAnalyzer : IDomainModelAnalyzer {
    public string Name => nameof(StageLineageAnalyzer);

    public void Analyze(Domain domain, DomainModelAnalysisContext context) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var entity in domain.GetAvailableEntities().Where(candidate => candidate is not Relationship)) {
            foreach (var stage in entity.Stages) {
                if (stage.Parent is null) {
                    continue;
                }

                var isValidParent = entity.Stages.Contains(stage.Parent)
                                    || (entity.ParentEntity is not null && entity.ParentEntity.Stages.Contains(stage.Parent));

                if (!isValidParent) {
                    context.ReportError(
                        code: "DM0002",
                        message: $"Stage '{stage.Name}' on entity '{entity.Name}' has parent '{stage.Parent.Name}' outside allowed lineage.",
                        location: $"Entity:{entity.Name}/Stage:{stage.Name}");
                }
            }
        }
    }
}