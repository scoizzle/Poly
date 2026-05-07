using Poly.Data.Modeling.Validation.Constraints;

namespace Poly.Data.Modeling.Analysis;

/// <summary>
/// Validates that EnumConstraint on properties is a subset of parent's EnumConstraint.
/// </summary>
internal sealed class EnumConstraintSubsetAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (node is Property property) {
            AnalyzeProperty(context, property);
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeProperty(AnalysisContext context, Property property) {
        if (!context.TryBeginAnalyzerVisit<EnumConstraintSubsetAnalyzer>(property)) return;

        var enumConstraint = property.Constraints.OfType<EnumConstraint>().FirstOrDefault()
            ?? property.Type.Constraints.OfType<EnumConstraint>().FirstOrDefault();
        if (enumConstraint is null) return;

        var entity = FindOwningEntity(property.Domain, property);
        if (entity?.ParentEntity is null) return;

        var parentProperty = FindPropertyInHierarchy(entity.ParentEntity, property.Name);
        if (parentProperty is null) return;

        var parentConstraint = parentProperty.Constraints.OfType<EnumConstraint>().FirstOrDefault()
            ?? parentProperty.Type.Constraints.OfType<EnumConstraint>().FirstOrDefault();
        if (parentConstraint is null) return;

        var propertyMembers = enumConstraint.Members.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var parentMembers = parentConstraint.Members.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);

        if (!propertyMembers.IsSubsetOf(parentMembers)) {
            var invalid = propertyMembers.Except(parentMembers);
            context.ReportError(
                property,
                $"EnumConstraint on '{property.Name}' ({string.Join(", ", invalid)}) is not a subset of parent's constraint.",
                DomainModelDiagnosticCodes.SemanticConstraintMismatch);
        }
    }

    private static Entity? FindOwningEntity(Domain domain, Property property) {
        return domain.Entities.FirstOrDefault(entity => entity.Properties.Contains(property));
    }

    private static Property? FindPropertyInHierarchy(Entity? entity, string name) {
        while (entity is not null) {
            var prop = entity.Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
            if (prop is not null) return prop;
            entity = entity.ParentEntity;
        }
        return null;
    }
}