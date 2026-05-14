namespace Poly.DomainModeling.V2;

public static class DomainRenderer {
    public static string Render(Domain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var lines = new List<string> { $"Domain: {domain.Name}" };

        foreach (var entity in domain.Entities) {
            lines.Add($"Entity: {entity.Name}");
            foreach (var property in entity.Properties) {
                var requiredSuffix = property.IsRequired ? " required" : string.Empty;
                lines.Add($"  Property: {property.Name} ({property.Type}){requiredSuffix}");
            }

            foreach (var stage in entity.Stages) {
                lines.Add($"  Stage: {stage.Name}{(stage.IsInitial ? " [initial]" : string.Empty)}");
            }

            foreach (var action in entity.Actions) {
                lines.Add($"  Action: {action.Name}");
                foreach (var effect in action.Effects) {
                    lines.Add($"    Effect: {effect.GetType().Name}");
                }
            }
        }

        foreach (var relationship in domain.Relationships) {
            lines.Add($"Relationship: {relationship.Name} ({relationship.SourceEntity} -> {relationship.TargetEntity}, {relationship.Kind})");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
