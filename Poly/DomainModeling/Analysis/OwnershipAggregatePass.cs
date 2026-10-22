using Poly.DomainModeling.Ontology;

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

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain) return;

        var topology = context.GetMetadata<EffectTopologyMetadata>(domain)?.Topology;
        var aggregate = BuildAggregate(domain, context, topology);
        context.SetMetadata(domain, new OwnershipAggregateMetadata(aggregate));
    }

    /// <summary>
    /// Builds <see cref="AggregateModel"/> — the cross-entity ownership hierarchy.
    /// Consumes <see cref="EntityStructureMetadata"/> for IsRoot detection and
    /// <see cref="EffectTopology"/> for create-in based parent prioritization.
    ///
    /// Invariant: a parentless entity is a root. An entity is never a non-root without a
    /// parent (that state has no key and no HTTP endpoints), and following parents always
    /// terminates at a root.
    /// </summary>
    internal static AggregateModel BuildAggregate(Domain domain, AnalysisContext? context, EffectTopology? topology = null) {
        var entities = domain.Types.OfType<Entity>().ToList();
        // Evolution/rollback may temporarily hold duplicate entity names; fail-closed like DomainCatalogPass.
        var entityLookup = entities
            .GroupBy(e => e.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var uniqueEntities = entityLookup.Values.ToList();
        var relationships = context is not null
            ? context.GetAllRelationships(domain).ToList()
            : uniqueEntities.SelectMany(e => e.Navigations).ToList();

        var incomingRels = new Dictionary<string, List<Relationship>>(StringComparer.Ordinal);
        foreach (var rel in relationships) {
            if (!incomingRels.TryGetValue(rel.Target.TypeName, out var list))
                incomingRels[rel.Target.TypeName] = list = new();
            list.Add(rel);
        }

        var drafts = new List<(string Name, bool IsRoot)>();
        foreach (var entity in uniqueEntities)
            drafts.Add((entity.Name, IsRootEntity(entity, context, entityLookup, relationships)));

        var resolved = new Dictionary<string, AggregateEntity>(StringComparer.Ordinal);
        foreach (var (name, isRoot) in drafts) {
            if (isRoot)
                resolved[name] = new AggregateEntity(name, isRoot: true);
        }

        // Resolve every non-root; a pass that resolves with no parent IS a root, promoted
        // immediately so a parentless owner (Member, which only references Plan) is
        // visible as a root to the children that name it — otherwise they skip it and end
        // up parentless too. Plain singulars never create parents here, so the only shapes
        // that can still cycle are mutual collections, which DMDEP001 reports.
        for (var pass = 0; pass < drafts.Count; pass++) {
            var changed = false;
            foreach (var (name, isRoot) in drafts) {
                if (isRoot) continue;
                var next = ResolveChild(name, resolved, incomingRels, relationships, entityLookup, topology);
                if (next.AggregateParentName is null)
                    next = new AggregateEntity(name, isRoot: true);
                var prev = resolved.GetValueOrDefault(name);
                if (prev is not null
                    && prev.IsRoot == next.IsRoot
                    && string.Equals(prev.AggregateParentName, next.AggregateParentName, StringComparison.Ordinal)
                    && string.Equals(prev.ParentRelationshipName, next.ParentRelationshipName, StringComparison.Ordinal))
                    continue;
                resolved[name] = next;
                changed = true;
            }
            if (!changed)
                break;
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

        // Keyed by creator AND relationship name: two owners may declare the same name
        // (`Member.bookings` and `Session.bookings`), and only the creator that actually
        // runs `create in` wrote the edge — matching on the name alone credits whichever
        // owner is declared first.
        var createInEdges = topo?.CreateInRelations
            .Where(c => string.Equals(c.CreatedEntity, name, StringComparison.Ordinal))
            .Select(c => (c.CreatorEntity, c.RelationshipName))
            .ToHashSet() ?? [];

        string? chosenParentName = null;
        string? chosenRelName = null;
        Relationship? chosenBackRef = null;

        foreach (var rel in incoming) {
            var isCollection = rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;
            if (!isCollection) continue;
            var parentEntity = entityLookup.GetValueOrDefault(rel.Source.TypeName);
            if (parentEntity is null) continue;
            var parentAgg = resolved.GetValueOrDefault(parentEntity.Name);
            // Root, or a non-root that is itself already aggregated (grandchild).
            // An unresolved parent, or one with no aggregate parent, is not a parent.
            if (parentAgg is null || (!parentAgg.IsRoot && parentAgg.AggregateParentName is null))
                continue;
            var backRef = relationships.FirstOrDefault(r =>
                string.Equals(r.Source.TypeName, name, StringComparison.Ordinal) &&
                string.Equals(r.Target.TypeName, parentEntity.Name, StringComparison.Ordinal) &&
                r.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany));
            if (createInEdges.Contains((rel.Source.TypeName, rel.Name))) {
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
            // The source of an inbound singular points at this entity (`Booking.member`,
            // `Schedule.doctor`), which reads as ownership for the classic 1:1 pair. Two
            // guards stop it from inventing a cycle:
            //  - a peer back-reference is the same edge declared twice (this entity
            //    already owns the source through a collection), so it is not a parent
            //    (Member.bookings + Booking.member made Member ⇄ Booking);
            //  - a source that already resolves to this entity as its parent would close
            //    a two-node loop (Doctor.schedule + Schedule.doctor), so it is skipped.
            var singular = incoming.FirstOrDefault(r =>
                r.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany)
                && entityLookup.ContainsKey(r.Source.TypeName)
                && !OwnsViaCollection(name, r.Source.TypeName, relationships)
                && !ResolvesTo(resolved, r.Source.TypeName, name));
            if (singular is not null) {
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

    /// <summary>
    /// True when <paramref name="owner"/> declares a collection navigation to
    /// <paramref name="target"/> — the target is owned through that collection, so a
    /// singular nav in the other direction is the same edge, not a parent.
    /// </summary>
    private static bool OwnsViaCollection(string owner, string target, List<Relationship> relationships) =>
        relationships.Any(r =>
            string.Equals(r.Source.TypeName, owner, StringComparison.Ordinal)
            && string.Equals(r.Target.TypeName, target, StringComparison.Ordinal)
            && r.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany);

    /// <summary>True when <paramref name="source"/> already resolves to <paramref name="owner"/> as its parent.</summary>
    private static bool ResolvesTo(Dictionary<string, AggregateEntity> resolved, string source, string owner) =>
        resolved.GetValueOrDefault(source)?.AggregateParentName is { } parent
        && string.Equals(parent, owner, StringComparison.Ordinal);

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