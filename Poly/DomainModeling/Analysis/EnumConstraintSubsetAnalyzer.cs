using Poly.DomainModeling.Constraints;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed class EnumConstraintSubsetAnalyzer : INodeAnalyzer {
    public const string Id = "DomainEnumConstraintSubsetAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [];
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


        return null;
    }
}