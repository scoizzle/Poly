using Poly.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;
using Poly.DomainModeling.Lowering;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Pre-computes <see cref="EntityStructureMetadata"/> for every entity in the domain.
///
/// This pass runs during the analysis pipeline so lowering passes (StorageAnalyzer,
/// AggregateAnalyzer) can consume the results without re-scanning properties and
/// constraints. The metadata captures entity-local structural properties:
/// root detection, key structure, soft-delete, and stage tracking.
///
/// Cross-entity parent resolution (aggregate ownership) remains in the lowering
/// layer because it depends on <see cref="EffectTopology"/>.
/// </summary>
internal sealed class EntityStructureAnalyzer : INodeAnalyzer {
    public const string Id = "DomainEntityStructureAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [DomainCatalogPass.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) return;

        if (node is Domain domain) {
            AnalyzeDomain(context, domain);
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        var lookup = context.GetTypeLookup(domain);
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
        // Map domain type → host CLR name for natural keys; shadow keys use int.
        var keyClrType = hasNaturalKey
            ? DomainTypeMapping.ToClrTypeName(uniqueProp!.Type.TypeName)
            : "int";

        // ── Root detection ────────────────────────────────────
        var isRoot = !HasRequiredEntityRef(entity, lookup);

        // ── Stage tracking ────────────────────────────────────
        var hasStages = entity.Stages.Count > 0;
        string? stageEnumTypeName = null;
        IReadOnlyDictionary<string, Stage>? stageByName = null;
        if (hasStages) {
            stageEnumTypeName = domain.Types
                .OfType<EnumType>()
                .FirstOrDefault(e => e.Name == $"{entity.Name}Stage")
                ?.Name ?? $"{entity.Name}Stage";
            stageByName = entity.Stages.ToDictionary(s => s.Name, StringComparer.Ordinal);
        }

        var constructorParameters = ComputeConstructorParameterOrder(entity, lookup);
        var entryAssignedPropertyNames = ComputeEntryAssignedPropertyNames(entity);

        // ── Enum-typed property map (property name → enum type name) ──
        Dictionary<string, string>? enumPropertyNames = null;
        foreach (var prop in entity.Properties) {
            if (lookup.Types.TryGetValue(prop.Type.TypeName, out var resolved)
                && resolved is EnumType) {
                (enumPropertyNames ??= new Dictionary<string, string>(StringComparer.Ordinal))[prop.Name] = prop.Type.TypeName;
            }
        }

        return new EntityStructureMetadata(
            isRoot, hasNaturalKey, keyPropName, keyClrType,
            hasStages, stageEnumTypeName, stageByName, constructorParameters,
            enumPropertyNames, entryAssignedPropertyNames
        );
    }

    /// <summary>
    /// Names of entity properties assigned by the FIRST stage's entry effects. The
    /// exported constructor runs those effects after setting CurrentStage, so these
    /// props are body-initialized — never ctor params (a param would be dead + written
    /// twice, e.g. StartedAt). Published on <see cref="EntityStructureMetadata.EntryAssignedPropertyNames"/>
    /// so the exporter's ctor emission and this signature stay in lockstep.
    /// </summary>
    internal static IReadOnlySet<string> ComputeEntryAssignedPropertyNames(Entity entity) {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (entity.Stages.Count > 0) {
            foreach (var effect in entity.Stages[0].OnEntryEffects) {
                if (effect is AssignEffect ae && ae.Target is PropertyAccess pa)
                    names.Add(pa.Name);
            }
        }
        return names;
    }

    private static IReadOnlyList<ConstructorParameterOrder> ComputeConstructorParameterOrder(
        Entity entity, DomainTypeLookupMetadata lookup) {
        var parameters = new List<ConstructorParameterOrder>();

        // Props assigned by the FIRST stage's entry effects are body-initialized in
        // the exported ctor (which runs those effects after setting CurrentStage) —
        // they are NOT ctor params. Shared rule published as
        // EntityStructureMetadata.EntryAssignedPropertyNames (single source of truth).
        var entryAssignedProps = ComputeEntryAssignedPropertyNames(entity);

        foreach (var prop in entity.Properties.OrderBy(p => p.Name)) {
            if (prop.Constraints.Any(c => c is DefaultValueConstraint)) continue;
            if (entryAssignedProps.Contains(prop.Name)) continue;
            parameters.Add(new ConstructorParameterOrder(prop.Name, prop.Type, IsNavigation: false, IsBackReference: false));
        }

        foreach (var rel in entity.Navigations) {
            var isMany = rel.Cardinality is RelationshipCardinality.OneToMany
                         or RelationshipCardinality.ManyToMany;
            if (string.Equals(rel.Target.TypeName, entity.Name, StringComparison.Ordinal)) {
                parameters.Add(new ConstructorParameterOrder(rel.Name, rel.Target, IsNavigation: true, IsBackReference: true, IsCollection: isMany));
                continue;
            }

            parameters.Add(new ConstructorParameterOrder(rel.Name, rel.Target, IsNavigation: true, IsBackReference: false, IsCollection: isMany));
        }

        return parameters;
    }

    /// <summary>
    /// Determines if an entity has a required entity reference in its
    /// constructor params (either as a property or an outgoing singular nav).
    /// </summary>
    private static bool HasRequiredEntityRef(Entity entity, DomainTypeLookupMetadata lookup) {
        if (entity.Properties.Any(p =>
            !p.Constraints.Any(c => c is DefaultValueConstraint) &&
            lookup.Types.TryGetValue(p.Type.TypeName, out var type) && type is Entity))
            return true;

        foreach (var rel in entity.Navigations) {
            if (rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany)
                continue;
            if (string.Equals(rel.Target.TypeName, entity.Name, StringComparison.Ordinal))
                continue;
            return true;
        }

        return false;
    }
}