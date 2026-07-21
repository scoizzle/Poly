using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Builds <see cref="AggregateModel"/> — the cross-entity ownership hierarchy.
///
/// Consumes <see cref="EntityStructureMetadata"/> for IsRoot detection and
/// <see cref="EffectTopology"/> for create-in based parent prioritization.
///
/// This is a derived domain fact, not a storage or transport convention.
/// </summary>
public sealed class AggregateAnalyzer {
    private readonly Domain _domain;
    private readonly List<Entity> _entities;
    private readonly List<Relationship> _relationships;
    private readonly Dictionary<string, Entity> _entityLookup;
    private readonly Dictionary<string, List<Relationship>> _incomingRels;
    private readonly AnalysisResult? _analysis;

    public AggregateAnalyzer(Domain domain, AnalysisResult? analysis = null) {
        _domain = domain;
        _analysis = analysis;

        var lookup = analysis?.GetMetadata<DomainTypeLookupMetadata>(default);
        if (lookup is not null) {
            _entities = lookup.Entities.ToList();
            _entityLookup = lookup.Types
                .Where(kvp => kvp.Value is Entity)
                .ToDictionary(kvp => kvp.Key, kvp => (Entity)kvp.Value, StringComparer.Ordinal);
        }
        else {
            _entities = domain.Types.OfType<Entity>().ToList();
            _entityLookup = _entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        }

        _relationships = domain.Relationships.ToList();
        _incomingRels = new Dictionary<string, List<Relationship>>(StringComparer.Ordinal);
        foreach (var rel in _relationships) {
            if (!_incomingRels.TryGetValue(rel.Target.TypeName, out var list))
                _incomingRels[rel.Target.TypeName] = list = new();
            list.Add(rel);
        }
    }

    public AggregateModel Analyze(EffectTopology? topology = null) {
        var aggEntities = new List<AggregateEntity>();
        var aggLookup = new Dictionary<string, AggregateEntity>(StringComparer.Ordinal);

        foreach (var entity in _entities) {
            var agg = new AggregateEntity(entity.Name);
            agg.IsRoot = IsRootEntity(entity);
            aggEntities.Add(agg);
            aggLookup[entity.Name] = agg;
        }

        // Pass 2: resolve parents
        foreach (var agg in aggEntities) {
            ResolveParent(agg, aggLookup, topology);
        }

        return new AggregateModel(_domain.Name, aggEntities);
    }

    private bool IsRootEntity(Entity entity) {
        var meta = _analysis?.GetMetadata<EntityStructureMetadata>(entity);
        if (meta is not null)
            return meta.IsRoot;

        return !HasRequiredEntityRef(entity);
    }

    private void ResolveParent(AggregateEntity agg, Dictionary<string, AggregateEntity> aggLookup, EffectTopology? topo) {
        if (agg.IsRoot) return;

        if (!_incomingRels.TryGetValue(agg.Name, out var incoming)) return;

        var createInRelNames = topo?.CreateInRelations
            .Where(c => string.Equals(c.CreatedEntity, agg.Name, StringComparison.Ordinal))
            .Select(c => c.RelationshipName)
            .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>();

        AggregateEntity? chosenParent = null;
        string? chosenRelName = null;
        Relationship? chosenBackRef = null;

        foreach (var rel in incoming) {
            var isCollection = rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;
            if (!isCollection) continue;

            var parentEntity = _entityLookup.GetValueOrDefault(rel.Source.TypeName);
            if (parentEntity is null) continue;

            var parentAgg = aggLookup.GetValueOrDefault(parentEntity.Name);
            if (parentAgg is null || !parentAgg.IsRoot) continue;

            var backRef = _relationships.FirstOrDefault(r =>
                string.Equals(r.Source.TypeName, agg.Name, StringComparison.Ordinal) &&
                string.Equals(r.Target.TypeName, parentEntity.Name, StringComparison.Ordinal) &&
                r.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany));

            if (createInRelNames.Contains(rel.Name)) {
                chosenParent = parentAgg;
                chosenRelName = rel.Name;
                chosenBackRef = backRef;
                break;
            }

            chosenParent ??= parentAgg;
            chosenRelName ??= rel.Name;
            chosenBackRef ??= backRef;
        }

        if (chosenParent is null && incoming.Count > 0) {
            var singular = incoming.FirstOrDefault(r =>
                r.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany));
            if (singular is not null && _entityLookup.ContainsKey(singular.Source.TypeName)) {
                chosenParent = aggLookup.GetValueOrDefault(singular.Source.TypeName);
                chosenRelName = singular.Name;
                chosenBackRef = null;
            }
        }

        if (chosenParent is not null) {
            agg.AggregateParentName = chosenParent.Name;
            agg.AggregateParent = chosenParent;
            agg.ParentRelationshipName = chosenRelName;
            agg.BackReferencePropertyName = chosenBackRef?.Name;
        }

        if (agg.AggregateParentName is null && topo is not null) {
            var createIn = topo.CreateInRelations.FirstOrDefault(c =>
                string.Equals(c.CreatedEntity, agg.Name, StringComparison.Ordinal));
            if (createIn is not null) {
                agg.AggregateParentName = createIn.CreatorEntity;
                agg.AggregateParent = aggLookup.GetValueOrDefault(createIn.CreatorEntity);
                agg.ParentRelationshipName = createIn.RelationshipName;
            }
        }
    }

    private bool HasRequiredEntityRef(Entity entity) {
        if (entity.Properties.Any(p => !p.Constraints.Any(c => c is DefaultValueConstraint)
            && _entityLookup.ContainsKey(p.Type.TypeName)))
            return true;
        if (_relationships.Any(r =>
            string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal) &&
            r.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany) &&
            !string.Equals(r.Target.TypeName, entity.Name, StringComparison.Ordinal)))
            return true;
        return false;
    }
}