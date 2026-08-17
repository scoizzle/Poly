using Poly.Analysis;
using Poly.DomainModeling.Constraints;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Analysis pass that produces <see cref="OwnershipAggregateMetadata"/> —
/// the cross-entity ownership hierarchy.
///
/// Consumes <see cref="EffectTopologyMetadata"/> for create-in parent prioritization
/// and <see cref="EntityStructureMetadata"/> for root detection.
/// Depends on <see cref="EffectTopologyPass"/> and <see cref="EntityStructureAnalyzer"/>.
/// </summary>
internal sealed class OwnershipAggregatePass : INodeAnalyzer {
    public const string Id = "OwnershipAggregatePass";
    public string PassName => Id;
    public string[] Dependencies => [EffectTopologyPass.Id, EntityStructureAnalyzer.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain) return;
        if (context.HasStructuralFailure) return;

        var topology = context.GetMetadata<EffectTopologyMetadata>(domain)?.Topology;
        var aggregate = BuildAggregate(domain, context, topology);
        context.SetMetadata(domain, new OwnershipAggregateMetadata(aggregate));

        // ── Diagnostics (DMAGG001) ─────────────────────────────
        var entities = domain.Types.OfType<Entity>().ToList();
        foreach (var e in entities) {
            var agg = aggregate.Entities.FirstOrDefault(a => a.Name == e.Name);
            if (agg is null) continue;
            if (!agg.IsRoot && agg.AggregateParentName is null) {
                context.ReportWarning(e,
                    $"Entity '{e.Name}' is a non-root entity with no aggregate parent. " +
                    "It may be orphaned — verify the relationship hierarchy or add a parent relationship.",
                    DomainModelDiagnosticCodes.AggregateOrphan);
            }
        }
    }

    /// <summary>
    /// Builds <see cref="AggregateModel"/> — the cross-entity ownership hierarchy.
    /// Consumes <see cref="EntityStructureMetadata"/> for IsRoot detection and
    /// <see cref="EffectTopology"/> for create-in based parent prioritization.
    /// </summary>
    internal static AggregateModel BuildAggregate(Domain domain, AnalysisContext? context, EffectTopology? topology = null) {
        var entities = domain.Types.OfType<Entity>().ToList();
        var entityLookup = entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var relationships = context is not null
            ? context.GetAllRelationships(domain).ToList()
            : entities.SelectMany(e => e.Navigations).ToList();

        var incomingRels = new Dictionary<string, List<Relationship>>(StringComparer.Ordinal);
        foreach (var rel in relationships) {
            if (!incomingRels.TryGetValue(rel.Target.TypeName, out var list))
                incomingRels[rel.Target.TypeName] = list = new();
            list.Add(rel);
        }

        var drafts = new List<(string Name, bool IsRoot)>();
        foreach (var entity in entities)
            drafts.Add((entity.Name, IsRootEntity(entity, context, entityLookup, relationships)));

        var resolved = new Dictionary<string, AggregateEntity>(StringComparer.Ordinal);
        foreach (var (name, isRoot) in drafts) {
            if (isRoot)
                resolved[name] = new AggregateEntity(name, isRoot: true);
        }

        foreach (var (name, isRoot) in drafts) {
            if (isRoot) continue;
            resolved[name] = ResolveChild(name, resolved, incomingRels, relationships, entityLookup, topology);
        }

        var ordered = drafts.Select(d => resolved[d.Name]).ToList();
        return new AggregateModel(domain.Name, ordered);
    }

    private static AggregateEntity ResolveChild(
        string name,
        Dictionary<string, AggregateEntity> resolved,
        Dictionary<string, List<Relationship>> incomingRels,
        List<Relationship> relationships,
        Dictionary<string, Entity> entityLookup,
        EffectTopology? topo) {
        if (!incomingRels.TryGetValue(name, out var incoming))
            return new AggregateEntity(name, isRoot: false);

        var createInRelNames = topo?.CreateInRelations
            .Where(c => string.Equals(c.CreatedEntity, name, StringComparison.Ordinal))
            .Select(c => c.RelationshipName)
            .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>();

        string? chosenParentName = null;
        string? chosenRelName = null;
        Relationship? chosenBackRef = null;

        foreach (var rel in incoming) {
            var isCollection = rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;
            if (!isCollection) continue;
            var parentEntity = entityLookup.GetValueOrDefault(rel.Source.TypeName);
            if (parentEntity is null) continue;
            var parentAgg = resolved.GetValueOrDefault(parentEntity.Name);
            if (parentAgg is null || !parentAgg.IsRoot) continue;
            var backRef = relationships.FirstOrDefault(r =>
                string.Equals(r.Source.TypeName, name, StringComparison.Ordinal) &&
                string.Equals(r.Target.TypeName, parentEntity.Name, StringComparison.Ordinal) &&
                r.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany));
            if (createInRelNames.Contains(rel.Name)) {
                chosenParentName = parentAgg.Name;
                chosenRelName = rel.Name;
                chosenBackRef = backRef;
                break;
            }
            chosenParentName ??= parentAgg.Name;
            chosenRelName ??= rel.Name;
            chosenBackRef ??= backRef;
        }

        if (chosenParentName is null && incoming.Count > 0) {
            var singular = incoming.FirstOrDefault(r =>
                r.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany));
            if (singular is not null && entityLookup.ContainsKey(singular.Source.TypeName)) {
                chosenParentName = singular.Source.TypeName;
                chosenRelName = singular.Name;
                chosenBackRef = null;
            }
        }

        if (chosenParentName is null && topo is not null) {
            var createIn = topo.CreateInRelations.FirstOrDefault(c =>
                string.Equals(c.CreatedEntity, name, StringComparison.Ordinal));
            if (createIn is not null) {
                chosenParentName = createIn.CreatorEntity;
                chosenRelName = createIn.RelationshipName;
            }
        }

        return new AggregateEntity(name, isRoot: false,
            aggregateParentName: chosenParentName,
            parentRelationshipName: chosenRelName,
            backReferencePropertyName: chosenBackRef?.Name,
            aggregateParent: chosenParentName is not null ? resolved.GetValueOrDefault(chosenParentName) : null);
    }

    private static bool IsRootEntity(Entity entity, AnalysisContext? context,
        Dictionary<string, Entity> entityLookup, List<Relationship> relationships) {
        // EntityStructureMetadata.IsRoot is the authoritative root signal from EntityStructureAnalyzer.
        // If metadata is absent (test/legacy path), fall back to structural heuristic.
        var meta = context?.GetStructure(entity);
        if (meta is not null)
            return meta.IsRoot;
        // Legacy fallback — no EntityStructureAnalyzer ran
        if (entity.Properties.Any(p => !p.Constraints.Any(c => c is DefaultValueConstraint)
            && entityLookup.ContainsKey(p.Type.TypeName)))
            return false;
        if (relationships.Any(r =>
            string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal) &&
            r.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany) &&
            !string.Equals(r.Target.TypeName, entity.Name, StringComparison.Ordinal)))
            return false;
        return true;
    }
}