namespace Poly.Data.Modeling.Analysis;

public sealed class RelationshipAttachmentAnalyzer : IDomainModelAnalyzer {
    public string Name => nameof(RelationshipAttachmentAnalyzer);

    public void Analyze(Domain domain, DomainModelAnalysisContext context) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var relationship in domain.Relationships) {
            if (relationship.Source is not Entity sourceEntity) {
                continue;
            }

            if (!sourceEntity.Relationships.Contains(relationship)) {
                context.ReportError(
                    code: "DM0001",
                    message: $"Relationship '{relationship.Name}' is not attached to source entity '{sourceEntity.Name}'.",
                    location: $"Relationship:{relationship.Name}");
            }
        }
    }
}