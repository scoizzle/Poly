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
        var owned = OwnedEdges(domain);
        var edges = owned
            .Select(static e => new OwnedEdge(e.Source.Name, e.Nav.Name, e.Nav.Target.TypeName, e.Nav.Cardinality))
            .ToList();
        context.SetMetadata(domain, new OwnershipAggregateMetadata(aggregate, edges));
        ReportOwnershipCycles(context, domain, owned);
    }

    /// <summary>
    /// V-14 (#586, OQ-14): a cycle of <c>owned</c> navigations through more than one entity
    /// type has no root, so it is an error: one per cycle the walk closes. A self-owning navigation is a
    /// tree and is not an edge here, also when its entity sits in a real cycle. The graph is
    /// <see cref="OwnedEdges"/>, the same definition of ownership DMSTR010 uses. The walk
    /// visits entities and their owned navigations in declaration order; each back edge is a
    /// cycle the walk closes, so a cycle that is only reachable through an already-finished
    /// entity is silent until the first is fixed (fixing the named navigation may reveal
    /// another; the document is rejected either way). A cycle is named from its earliest-declared entity (so <c>A -&gt; B -&gt; A</c> and
    /// <c>B -&gt; A -&gt; B</c> are one cycle, reported once) and positioned on the owned
    /// navigation that returns to that entity.
    /// </summary>
    private static void ReportOwnershipCycles(
        AnalysisContext context, Domain domain, IReadOnlyList<(Entity Source, Relationship Nav)> owned) {
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entity in domain.Types.OfType<Entity>())
            order.TryAdd(entity.Name, order.Count);

        var outgoing = new Dictionary<string, List<(Entity Source, Relationship Nav)>>(StringComparer.Ordinal);
        foreach (var edge in owned) {
            if (string.Equals(edge.Source.Name, edge.Nav.Target.TypeName, StringComparison.Ordinal))
                continue;
            if (!outgoing.TryGetValue(edge.Source.Name, out var list))
                outgoing[edge.Source.Name] = list = new();
            list.Add(edge);
        }

        // 1 = on the walk's stack, 2 = finished.
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in order.Keys.OrderBy(k => order[k])) {
            if (state.ContainsKey(root))
                continue;
            var path = new List<string> { root };
            var viaEdges = new List<(Entity Source, Relationship Nav)>();
            var next = new List<int> { 0 };
            state[root] = 1;
            while (path.Count > 0) {
                var top = path.Count - 1;
                var edges = outgoing.GetValueOrDefault(path[top]);
                if (edges is null || next[top] >= edges.Count) {
                    state[path[top]] = 2;
                    path.RemoveAt(top);
                    next.RemoveAt(top);
                    if (viaEdges.Count > 0)
                        viaEdges.RemoveAt(viaEdges.Count - 1);
                    continue;
                }
                var edge = edges[next[top]++];
                var target = edge.Nav.Target.TypeName;
                switch (state.GetValueOrDefault(target)) {
                    case 0:
                        state[target] = 1;
                        path.Add(target);
                        viaEdges.Add(edge);
                        next.Add(0);
                        break;
                    case 1:
                        var start = path.IndexOf(target);
                        ReportCycle(context, order, path.Skip(start).ToList(), [.. viaEdges.Skip(start), edge], reported);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Reports one cycle: <paramref name="members"/> in walk order, <paramref name="cycleEdges"/>
    /// the owned navigation leaving each member for the next (the last returns to the first).
    /// </summary>
    private static void ReportCycle(
        AnalysisContext context,
        Dictionary<string, int> order,
        List<string> members,
        List<(Entity Source, Relationship Nav)> cycleEdges,
        HashSet<string> reported) {
        var first = 0;
        for (var i = 1; i < members.Count; i++)
            if (order[members[i]] < order[members[first]])
                first = i;
        var names = Enumerable.Range(0, members.Count).Select(i => members[(first + i) % members.Count]).ToList();
        var navs = Enumerable.Range(0, members.Count).Select(i => cycleEdges[(first + i) % members.Count]).ToList();
        var path = string.Join(" -> ", names.Append(names[0]));
        if (!reported.Add(path))
            return;
        var closing = navs[^1];
        context.ReportError(
            closing.Nav,
            $"Ownership cycle {path}: each entity owns the next through an 'owned' navigation " +
            $"({string.Join(", ", navs.Select(static n => $"'{n.Source.Name}.{n.Nav.Name}'"))}), so none of them is a root. " +
            "Make one of these navigations a plain reference; an entity owning only itself is allowed. " +
            $"Reported on '{closing.Source.Name}.{closing.Nav.Name}', the owned navigation that returns to '{names[0]}', " +
            "the earliest-declared entity of the cycle.",
            DomainModelDiagnosticCodes.OwnershipCycle);
    }

    /// <summary>
    /// The one owned-edge computation (S-12): every <c>owned</c> navigation in declaration
    /// order. A source or target type declared more than once failed admission
    /// (DMSTR001) and is skipped rather than cascaded on.
    /// </summary>
    internal static IReadOnlyList<(Entity Source, Relationship Nav)> OwnedEdges(Domain domain) {
        var entities = domain.Types.OfType<Entity>().ToList();
        var counts = entities.GroupBy(static e => e.Name, StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => g.Count(), StringComparer.Ordinal);
        var edges = new List<(Entity, Relationship)>();
        foreach (var entity in entities) {
            if (counts[entity.Name] != 1) continue;
            foreach (var nav in entity.Navigations) {
                if (nav.SourceOwnsTarget && counts.GetValueOrDefault(nav.Target.TypeName) == 1)
                    edges.Add((entity, nav));
            }
        }
        return edges;
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
        // that can still cycle are mutual collections; owned ones are DMDEP002 (V-14), and
        // the ResolvesTo guard keeps this loop finite for the rest.
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
        // The same fact EntityStructureAnalyzer reads (L-15a): only a required to-one names an owner.
        if (relationships.Any(r =>
            string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal) &&
            r.IsRequiredToOne() &&
            !string.Equals(r.Target.TypeName, entity.Name, StringComparison.Ordinal)))
            return false;
        return true;
    }
}