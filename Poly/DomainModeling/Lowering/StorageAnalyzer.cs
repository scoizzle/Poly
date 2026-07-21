using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Builds <see cref="StorageModel"/> — storage conventions applied to
/// shared domain facts (EntityStructureMetadata, AggregateModel).
///
/// Produces columns, navigations, foreign keys, soft-delete storage shape,
/// stage tracking storage shape, and subscription list backing fields.
/// </summary>
public sealed class StorageAnalyzer {
    private readonly Domain _domain;
    private readonly List<Entity> _entities;
    private readonly List<Relationship> _relationships;
    private readonly Dictionary<string, Entity> _entityLookup;
    private readonly AnalysisResult? _analysis;

    public StorageAnalyzer(Domain domain, AnalysisResult? analysis = null) {
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
    }

    public StorageModel Analyze(AggregateModel? aggregate = null, EffectTopology? topology = null) {
        var aggLookup = aggregate?.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var storageEntities = new List<StorageEntity>();

        foreach (var entity in _entities) {
            storageEntities.Add(BuildStorageEntity(entity, aggLookup, topology));
        }

        var rels = _relationships.Select(r => new StorageRelationship(r)).ToList();
        return new StorageModel(_domain.Name, storageEntities, rels);
    }

    private StorageEntity BuildStorageEntity(Entity entity,
        Dictionary<string, AggregateEntity>? aggLookup, EffectTopology? topology) {

        var store = new StorageEntity(entity);
        var meta = _analysis?.GetMetadata<EntityStructureMetadata>(entity);
        var agg = aggLookup?.GetValueOrDefault(entity.Name);

        // ── Entity structure (from metadata or fallback) ──────
        if (meta is not null) {
            store.KeyProperty = meta.KeyPropertyName is not null
                ? entity.Properties.FirstOrDefault(p =>
                    string.Equals(p.Name, meta.KeyPropertyName, StringComparison.Ordinal))
                : null;
            store.KeyName = meta.KeyPropertyName is not null
                ? ToCamelCase(meta.KeyPropertyName) : "id";
            store.KeyClrType = meta.KeyClrType;
            store.IsRoot = meta.IsRoot;
            store.HasSoftDelete = meta.HasSoftDelete;
            if (meta.HasStages) {
                store.HasStages = true;
                store.StagePropertyName = "CurrentStage";
                store.StageEnumTypeName = meta.StageEnumTypeName;
            }
        }
        else {
            var uniqueProp = entity.Properties.FirstOrDefault(p =>
                p.Constraints.Any(c => c is UniqueConstraint));
            store.KeyProperty = uniqueProp;
            store.KeyName = uniqueProp is not null ? ToCamelCase(uniqueProp.Name) : "id";
            store.KeyClrType = uniqueProp is not null ? "string" : "int";
            store.IsRoot = !HasRequiredEntityRef(entity);
            store.HasSoftDelete = entity.Properties.Any(p =>
                string.Equals(p.Name, "IsDeleted", StringComparison.Ordinal) &&
                string.Equals(p.Type.TypeName, "Boolean", StringComparison.Ordinal) &&
                p.Constraints.Any(c => c is DefaultValueConstraint));
            var stageEnumType = _domain.Types.OfType<EnumType>()
                .FirstOrDefault(e => e.Name == $"{entity.Name}Stage");
            if (stageEnumType is not null || entity.Stages.Count > 0) {
                store.HasStages = true;
                store.StagePropertyName = "CurrentStage";
                store.StageEnumTypeName = stageEnumType?.Name ?? $"{entity.Name}Stage";
            }
        }

        // ── Aggregate parent (from pre-computed AggregateModel) ─
        if (agg is not null) {
            store.AggregateParentName = agg.AggregateParentName;
        }

        // ── Columns / navigations ─────────────────────────────
        ClassifyProperties(entity, store);

        // ── Subscription lists ────────────────────────────────
        DetectSubscriptionLists(store, topology);

        return store;
    }

    private void ClassifyProperties(Entity entity, StorageEntity store) {
        var enumTypes = _domain.Types.OfType<EnumType>()
            .ToDictionary(e => e.Name, StringComparer.Ordinal);

        foreach (var prop in entity.Properties) {
            var isEntityRef = _entityLookup.ContainsKey(prop.Type.TypeName);
            var isEnum = enumTypes.ContainsKey(prop.Type.TypeName);

            if (isEntityRef) continue;

            var col = new StorageColumn(prop, GetColumnType(prop, isEnum)) {
                ClrTypeName = GetClrTypeName(prop.Type.TypeName),
                IsEnum = isEnum,
                IsRequired = prop.Constraints.Any(c => c is RequiredConstraint),
                HasDefault = prop.Constraints.Any(c => c is DefaultValueConstraint),
                IsUnique = prop.Constraints.Any(c => c is UniqueConstraint),
                MaxLength = prop.Constraints.OfType<LengthConstraint>().FirstOrDefault()?.MaxLength,
            };
            store.AddColumn(col);
        }

        foreach (var rel in _relationships) {
            if (!string.Equals(rel.Source.TypeName, entity.Name, StringComparison.Ordinal)) continue;
            var isCollection = rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;
            var nav = new StorageNavigation(rel, ToPascalCase(rel.Name), isCollection);
            if (isCollection) store.AddCollectionNavigation(nav);
            else store.AddReferenceNavigation(nav);
        }
    }

    private void DetectSubscriptionLists(StorageEntity store, EffectTopology? topo) {
        if (topo is null) return;

        var subsBySubscriber = topo.Subscriptions
            .Where(s => string.Equals(s.SubscriberEntity, store.Name, StringComparison.Ordinal))
            .GroupBy(s => s.RelationshipName, StringComparer.Ordinal);

        foreach (var group in subsBySubscriber) {
            var nav = store.CollectionNavigations
                .FirstOrDefault(n => string.Equals(n.PropertyName, ToPascalCase(group.Key), StringComparison.Ordinal));
            if (nav is null) continue;

            var events = group.Select(s => s.TargetStage).Distinct().ToList();
            store.AddSubscriptionList(new StorageSubscriptionList(
                ToPascalCase(group.Key), store.Name, events));
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

    private static string GetColumnType(Property prop, bool isEnum) {
        if (isEnum) return prop.Type.TypeName;
        return prop.Type.TypeName switch {
            "Text" or "String" => "nvarchar(max)",
            "Number" or "Int" or "Int64" => "bigint",
            "Int32" => "int",
            "Boolean" or "Bool" => "bit",
            "DateTime" or "Timestamp" => "datetime2",
            "Date" or "DateOnly" => "date",
            "Time" or "TimeOnly" => "time",
            "Duration" or "TimeSpan" => "time",
            "Decimal" => "decimal(18,6)",
            "Float" or "Double" => "float",
            "Guid" or "Uuid" => "uniqueidentifier",
            _ => "nvarchar(max)",
        };
    }

    private static string GetClrTypeName(string domainType) => domainType switch {
        "Text" or "String" => "string",
        "Number" or "Int" or "Int64" => "long",
        "Int32" => "int",
        "Boolean" or "Bool" => "bool",
        "DateTime" or "Timestamp" => "DateTime",
        "Date" or "DateOnly" => "DateOnly",
        "Time" or "TimeOnly" => "TimeOnly",
        "Duration" or "TimeSpan" => "TimeSpan",
        "Decimal" => "decimal",
        "Float" or "Double" => "double",
        "Guid" or "Uuid" => "Guid",
        _ => "string",
    };

    private static string ToCamelCase(string name) {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0])) return name;
        int upperCount = 0;
        for (int i = 0; i < name.Length && char.IsUpper(name[i]); i++) upperCount++;
        if (upperCount <= 1) return char.ToLowerInvariant(name[0]) + name.Substring(1);
        return name.Substring(0, upperCount).ToLowerInvariant() + name.Substring(upperCount);
    }

    private static string ToPascalCase(string name) {
        if (string.IsNullOrEmpty(name) || char.IsUpper(name[0])) return name;
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }
}