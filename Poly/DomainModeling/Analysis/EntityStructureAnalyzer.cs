using Poly.DomainModeling.Constraints;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Pre-computes <see cref="EntityStructureMetadata"/> for every entity in the domain.
///
/// This pass runs during the analysis pipeline so lowering passes (StorageAnalyzer,
/// TransportAnalyzer) can consume the results without re-scanning properties and
/// constraints. The metadata captures entity-local structural properties:
/// root detection, key structure, soft-delete, and stage tracking.
///
/// Cross-entity parent resolution (aggregate ownership) remains in the lowering
/// layer because it depends on <see cref="Lowering.EffectTopology"/>.
/// </summary>
internal sealed class EntityStructureAnalyzer : INodeAnalyzer {
    public const string Id = "DomainEntityStructureAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [SemanticDomainAnalyzer.Id]; // needs DomainTypeLookupMetadata

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) return;

        if (node is Domain domain) {
            AnalyzeDomain(context, domain);
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        if (!context.TryBeginAnalyzerVisit<EntityStructureAnalyzer>(domain)) return;

        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        if (lookup is null) return;

        foreach (var entity in lookup.Entities) {
            var metadata = ComputeStructure(entity, domain, lookup);
            context.SetMetadata(entity, metadata);
        }
    }

    private static EntityStructureMetadata ComputeStructure(
        Entity entity, Domain domain, DomainTypeLookupMetadata lookup) {

        // ── Natural key detection ──────────────────────────────
        var uniqueProp = entity.Properties.FirstOrDefault(p =>
            p.Constraints.Any(c => c is UniqueConstraint));
        var hasNaturalKey = uniqueProp is not null;
        var keyPropName = uniqueProp?.Name;
        var keyClrType = hasNaturalKey ? "string" : "int";

        // ── Root detection ────────────────────────────────────
        var isRoot = !HasRequiredEntityRef(entity, lookup);

        // ── Soft delete ───────────────────────────────────────
        var hasSoftDelete = entity.Properties.Any(p =>
            string.Equals(p.Name, "IsDeleted", StringComparison.Ordinal) &&
            string.Equals(p.Type.TypeName, "Boolean", StringComparison.Ordinal) &&
            p.Constraints.Any(c => c is DefaultValueConstraint));

        // ── Stage tracking ────────────────────────────────────
        var hasStages = entity.Stages.Count > 0;
        string? stageEnumTypeName = null;
        if (hasStages) {
            stageEnumTypeName = domain.Types
                .OfType<EnumType>()
                .FirstOrDefault(e => e.Name == $"{entity.Name}Stage")
                ?.Name ?? $"{entity.Name}Stage";
        }

        return new EntityStructureMetadata(
            isRoot, hasNaturalKey, keyPropName, keyClrType,
            hasSoftDelete, hasStages, stageEnumTypeName
        );
    }

    /// <summary>
    /// Determines if an entity has a required entity reference in its
    /// constructor params (either as a property or an incoming singular nav).
    /// Reuses <see cref="DomainTypeLookupMetadata"/> to avoid re-scanning.
    /// </summary>
    private static bool HasRequiredEntityRef(Entity entity, DomainTypeLookupMetadata lookup) {
        // Entity property whose type is another entity (check via type name)
        if (entity.Properties.Any(p =>
            !p.Constraints.Any(c => c is DefaultValueConstraint) &&
            lookup.Types.TryGetValue(p.Type.TypeName, out var type) && type is Entity))
            return true;

        // Incoming singular navigations from this entity to another
        // e.g. borrower: Patron on Loan means Loan has a required ref to Patron
        // This checks relationships where this entity is the source.
        foreach (var rel in lookup.Domain.Relationships) {
            if (!string.Equals(rel.Source.TypeName, entity.Name, StringComparison.Ordinal))
                continue;
            if (rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany)
                continue;
            if (string.Equals(rel.Target.TypeName, entity.Name, StringComparison.Ordinal))
                continue;
            return true;
        }

        return false;
    }
}