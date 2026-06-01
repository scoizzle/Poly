using Poly.DomainModeling.Constraints;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed class EnumConstraintSubsetAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        if (node is Property property) {
            ValidateProperty(context, property);
        }

        this.AnalyzeChildren(context, node);
    }

    private static void ValidateProperty(AnalysisContext context, Property property) {
        if (!context.TryBeginAnalyzerVisit<EnumConstraintSubsetAnalyzer>(property)) {
            return;
        }

        var enumConstraint = property.Constraints.OfType<EnumConstraint>().FirstOrDefault();
        if (enumConstraint is null) {
            return;
        }

        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        if (lookup is null) return;

        var parentEntity = ResolveParentEntity(property, lookup);
        if (parentEntity is null) return;

        var parentProperty = FindPropertyInHierarchy(parentEntity, property.Name, lookup);
        if (parentProperty is null) return;

        var parentEnum = parentProperty.Constraints.OfType<EnumConstraint>().FirstOrDefault();
        if (parentEnum is null) return;

        var childMembers = enumConstraint.Members.Select(static m => m.Name).ToHashSet(StringComparer.Ordinal);
        var parentMembers = parentEnum.Members.Select(static m => m.Name).ToHashSet(StringComparer.Ordinal);

        if (!childMembers.IsSubsetOf(parentMembers)) {
            var invalid = childMembers.Except(parentMembers);
            context.ReportError(
                property,
                $"EnumConstraint on '{property.Name}' ({string.Join(", ", invalid)}) is not a subset of parent entity's constraint.",
                DomainModelDiagnosticCodes.SemanticConstraintMismatch);
        }
    }

    private static Entity? ResolveParentEntity(Property property, DomainTypeLookupMetadata lookup) {
        foreach (var entity in lookup.Entities) {
            if (entity.Properties.Contains(property) && entity.ParentEntityName is not null) {
                if (lookup.Types.TryGetValue(entity.ParentEntityName, out var parentType) && parentType is Entity parentEntity) {
                    return parentEntity;
                }
                return null;
            }
        }

        return null;
    }

    private static Property? FindPropertyInHierarchy(
        Entity entity, string propertyName, DomainTypeLookupMetadata lookup, HashSet<NodeId>? visited = null) {
        visited ??= [];
        if (!visited.Add(entity.Id)) return null;

        foreach (var prop in entity.Properties) {
            if (string.Equals(prop.Name, propertyName, StringComparison.Ordinal)) {
                return prop;
            }
        }

        if (entity.ParentEntityName is not null
            && lookup.Types.TryGetValue(entity.ParentEntityName, out var parentType)
            && parentType is Entity parentEntity) {
            return FindPropertyInHierarchy(parentEntity, propertyName, lookup, visited);
        }

        return null;
    }
}