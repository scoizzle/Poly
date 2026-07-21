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
        var drafts = new List<(string Name, bool IsRoot)>();
        foreach (var entity in _entities)
            drafts.Add((entity.Name, IsRootEntity(entity)));

        var resolved = new Dictionary<string, AggregateEntity>(StringComparer.Ordinal);
        foreach (var (name, isRoot) in drafts) {
            if (isRoot)
                resolved[name] = new AggregateEntity(name, isRoot: true);
        }

        foreach (var (name, isRoot) in drafts) {
            if (isRoot) continue;
            resolved[name] = ResolveChild(name, resolved, topology);
        }

        var ordered = drafts.Select(d => resolved[d.Name]).ToList();
        return new AggregateModel(_domain.Name, ordered);
    }

    private AggregateEntity ResolveChild(
        string name,
        Dictionary<string, AggregateEntity> resolved,
        EffectTopology? topo) {
        if (!_incomingRels.TryGetValue(name, out var incoming))
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

            var parentEntity = _entityLookup.GetValueOrDefault(rel.Source.TypeName);
            if (parentEntity is null) continue;

            var parentAgg = resolved.GetValueOrDefault(parentEntity.Name);
            if (parentAgg is null || !parentAgg.IsRoot) continue;

            var backRef = _relationships.FirstOrDefault(r =>
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
            if (singular is not null && _entityLookup.ContainsKey(singular.Source.TypeName)) {
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

        return new AggregateEntity(
            name,
            isRoot: false,
            aggregateParentName: chosenParentName,
            parentRelationshipName: chosenRelName,
            backReferencePropertyName: chosenBackRef?.Name,
            aggregateParent: chosenParentName is not null
                ? resolved.GetValueOrDefault(chosenParentName)
                : null);
    }

    private bool IsRootEntity(Entity entity) {
        var meta = _analysis?.GetMetadata<EntityStructureMetadata>(entity);
        if (meta is not null)
            return meta.IsRoot;

        return !HasRequiredEntityRef(entity);
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