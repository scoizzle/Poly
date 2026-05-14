namespace Poly.DomainModeling.V2;

public sealed record DomainValidationIssue(string Code, string Message);

public sealed record DomainValidationResult(IReadOnlyList<DomainValidationIssue> Issues) {
    public bool IsValid => Issues.Count == 0;
}

public static class DomainValidator {
    public static DomainValidationResult Validate(Domain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var issues = new List<DomainValidationIssue>();
        var entityNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in domain.Entities) {
            if (!entityNames.Add(entity.Name)) {
                issues.Add(new DomainValidationIssue("DuplicateEntity", $"Entity '{entity.Name}' already exists."));
            }

            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in entity.Properties) {
                if (!propertyNames.Add(property.Name)) {
                    issues.Add(new DomainValidationIssue("DuplicateProperty", $"Entity '{entity.Name}' has duplicate property '{property.Name}'."));
                }
            }

            var initialStageCount = entity.Stages.Count(stage => stage.IsInitial);
            if (initialStageCount > 1) {
                issues.Add(new DomainValidationIssue("MultipleInitialStages", $"Entity '{entity.Name}' has more than one initial stage."));
            }
        }

        foreach (var relationship in domain.Relationships) {
            if (!entityNames.Contains(relationship.SourceEntity)) {
                issues.Add(new DomainValidationIssue("UnknownSourceEntity", $"Relationship '{relationship.Name}' source entity '{relationship.SourceEntity}' does not exist."));
            }

            if (!entityNames.Contains(relationship.TargetEntity)) {
                issues.Add(new DomainValidationIssue("UnknownTargetEntity", $"Relationship '{relationship.Name}' target entity '{relationship.TargetEntity}' does not exist."));
            }
        }

        return new DomainValidationResult(issues);
    }
}
