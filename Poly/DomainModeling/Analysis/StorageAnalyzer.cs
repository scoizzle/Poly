using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Lowering;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Builds <see cref="StorageModel"/> — storage conventions applied to
/// shared domain facts (EntityStructureMetadata, AggregateModel).
///
/// Produces columns, navigations, foreign keys, soft-delete storage shape,
/// stage tracking storage shape, and subscription list backing fields.
///
/// P2: Accepts an optional <see cref="TypeMappingRegistry"/> for per-pack
/// type overrides and an optional <see cref="IStorageConvention"/> chain
/// for post-processing. When <c>column</c> / <c>table</c> annotations are
/// present on entity/property facets, they are applied to override the
/// baseline column name, column type, and entity table name.
/// </summary>
public sealed class StorageAnalyzer {
    private readonly Domain _domain;
    private readonly List<Entity> _entities;
    private readonly List<Relationship> _relationships;
    private readonly Dictionary<string, Entity> _entityLookup;
    private readonly AnalysisResult? _analysis;
    private readonly TypeMappingRegistry _typeMaps;
    private readonly IReadOnlyList<IStorageConvention> _conventions;

    public StorageAnalyzer(
        Domain domain,
        AnalysisResult? analysis = null,
        TypeMappingRegistry? typeMaps = null,
        IReadOnlyList<IStorageConvention>? conventions = null) {
        _domain = domain;
        _analysis = analysis;
        _typeMaps = typeMaps ?? new TypeMappingRegistry();
        _conventions = conventions ?? [];

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

        foreach (var entity in _entities)
            storageEntities.Add(BuildStorageEntity(entity, aggLookup, topology));

        var rels = _relationships.Select(r => new StorageRelationship(r)).ToList();
        return new StorageModel(_domain.Name, storageEntities, rels);
    }

    /// <summary>Recognised annotation keywords for storage facets.</summary>
    internal const string ColumnAnnotationKeyword = "column";
    internal const string TableAnnotationKeyword = "table";

    /// <summary>
    /// Reads the last <c>column("NAME" [, "TYPE"])</c> on property facets (last wins).
    /// Empty/whitespace names fail closed.
    /// </summary>
    internal static (string? ColumnName, string? ColumnType) ResolveColumnAnnotation(Property property) {
        ArgumentNullException.ThrowIfNull(property);
        string? name = null;
        string? type = null;
        var sawColumn = false;

        foreach (var facet in property.Facets) {
            if (facet is not Annotation ann
                || !string.Equals(ann.Name, ColumnAnnotationKeyword, StringComparison.Ordinal)) {
                continue;
            }

            sawColumn = true;
            name = null;
            type = null;

            if (ann.Arguments.TryGetValue("0", out var arg0)) {
                if (arg0 is not AnnotationString nameStr || string.IsNullOrWhiteSpace(nameStr.Value)) {
                    throw new InvalidOperationException(
                        $"Property '{property.Name}': column annotation argument 0 must be a non-empty string.");
                }
                name = nameStr.Value;
            }
            else {
                throw new InvalidOperationException(
                    $"Property '{property.Name}': column annotation requires a non-empty name argument.");
            }

            if (ann.Arguments.TryGetValue("1", out var arg1)) {
                if (arg1 is not AnnotationString typeStr || string.IsNullOrWhiteSpace(typeStr.Value)) {
                    throw new InvalidOperationException(
                        $"Property '{property.Name}': column annotation argument 1 must be a non-empty string when present.");
                }
                type = typeStr.Value;
            }
        }

        return sawColumn ? (name, type) : (null, null);
    }

    /// <summary>
    /// Reads the last <c>table("NAME")</c> on entity facets (last wins).
    /// Empty/whitespace names fail closed.
    /// </summary>
    internal static string? ResolveTableAnnotation(Entity entity) {
        ArgumentNullException.ThrowIfNull(entity);
        string? tableName = null;
        var sawTable = false;

        foreach (var facet in entity.Facets) {
            if (facet is not Annotation ann
                || !string.Equals(ann.Name, TableAnnotationKeyword, StringComparison.Ordinal)) {
                continue;
            }

            sawTable = true;
            if (!ann.Arguments.TryGetValue("0", out var arg0)
                || arg0 is not AnnotationString nameStr
                || string.IsNullOrWhiteSpace(nameStr.Value)) {
                throw new InvalidOperationException(
                    $"Entity '{entity.Name}': table annotation requires a non-empty name argument.");
            }
            tableName = nameStr.Value;
        }

        return sawTable ? tableName : null;
    }

    private StorageEntity BuildStorageEntity(
        Entity entity,
        Dictionary<string, AggregateEntity>? aggLookup,
        EffectTopology? topology) {
        var meta = _analysis?.GetMetadata<EntityStructureMetadata>(entity);
        var agg = aggLookup?.GetValueOrDefault(entity.Name);

        Property? keyProperty;
        string keyName;
        string keyClrType;
        bool isRoot;
        bool hasSoftDelete;
        bool hasStages;
        string? stagePropertyName = null;
        string? stageEnumTypeName = null;

        if (meta is not null) {
            keyProperty = meta.KeyPropertyName is not null
                ? entity.Properties.FirstOrDefault(p =>
                    string.Equals(p.Name, meta.KeyPropertyName, StringComparison.Ordinal))
                : null;
            keyName = meta.KeyPropertyName is not null
                ? DomainTypeMapping.ToCamelCase(meta.KeyPropertyName) : "id";
            keyClrType = meta.KeyClrType;
            // Aggregate owns hierarchy; prefer it when present.
            isRoot = agg?.IsRoot ?? meta.IsRoot;
            hasSoftDelete = meta.HasSoftDelete;
            hasStages = meta.HasStages;
            if (hasStages) {
                stagePropertyName = "CurrentStage";
                stageEnumTypeName = meta.StageEnumTypeName;
            }
        }
        else {
            var uniqueProp = entity.Properties.FirstOrDefault(p =>
                p.Constraints.Any(c => c is UniqueConstraint));
            keyProperty = uniqueProp;
            keyName = uniqueProp is not null
                ? DomainTypeMapping.ToCamelCase(uniqueProp.Name) : "id";
            keyClrType = uniqueProp is not null
                ? DomainTypeMapping.ToClrTypeName(uniqueProp.Type.TypeName) : "int";
            isRoot = agg?.IsRoot ?? !HasRequiredEntityRef(entity);
            hasSoftDelete = entity.Properties.Any(p =>
                string.Equals(p.Name, "IsDeleted", StringComparison.Ordinal) &&
                string.Equals(p.Type.TypeName, "Boolean", StringComparison.Ordinal) &&
                p.Constraints.Any(c => c is DefaultValueConstraint));
            var stageEnumType = _domain.Types.OfType<EnumType>()
                .FirstOrDefault(e => e.Name == $"{entity.Name}Stage");
            if (stageEnumType is not null || entity.Stages.Count > 0) {
                hasStages = true;
                stagePropertyName = "CurrentStage";
                stageEnumTypeName = stageEnumType?.Name ?? $"{entity.Name}Stage";
            }
            else {
                hasStages = false;
            }
        }

        var aggregateParentName = agg?.AggregateParentName;
        var (columns, collectionNavs, referenceNavs) = ClassifyProperties(entity);
        var foreignKeys = BuildForeignKeys(entity, agg, aggLookup);
        var subscriptionLists = DetectSubscriptionLists(entity.Name, collectionNavs, topology);

        // P2: Apply table annotation override
        var tableName = ResolveTableAnnotation(entity);

        var storageEntity = new StorageEntity(
            entity,
            keyName,
            keyClrType,
            keyProperty,
            isRoot,
            aggregateParentName,
            hasSoftDelete,
            hasStages,
            stagePropertyName,
            stageEnumTypeName,
            columns,
            collectionNavs,
            referenceNavs,
            foreignKeys,
            subscriptionLists,
            tableName);

        // P2: Apply convention chain
        foreach (var conv in _conventions) {
            var projected = conv.ProjectEntity(entity, storageEntity);
            if (projected is not null)
                storageEntity = projected;
        }

        return storageEntity;
    }

    private (List<StorageColumn> Columns, List<StorageNavigation> Collections, List<StorageNavigation> References)
        ClassifyProperties(Entity entity) {
        var enumTypes = _domain.Types.OfType<EnumType>()
            .ToDictionary(e => e.Name, StringComparer.Ordinal);
        var columns = new List<StorageColumn>();
        var collections = new List<StorageNavigation>();
        var references = new List<StorageNavigation>();

        foreach (var prop in entity.Properties) {
            var isEntityRef = _entityLookup.ContainsKey(prop.Type.TypeName);
            if (isEntityRef) continue;

            var isEnum = enumTypes.ContainsKey(prop.Type.TypeName);

            // P2: Default column type from registry, then apply column annotation override
            var baseColumnType = isEnum
                ? prop.Type.TypeName
                : _typeMaps.ToSqlColumnType(prop.Type.TypeName);
            var baseClrType = isEnum
                ? prop.Type.TypeName
                : _typeMaps.ToClrTypeName(prop.Type.TypeName);

            var (colName, colType) = ResolveColumnAnnotation(prop);
            var columnType = colType ?? baseColumnType;

            var column = new StorageColumn(
                prop,
                columnType,
                baseClrType,
                isEnum,
                prop.Constraints.Any(c => c is RequiredConstraint),
                prop.Constraints.Any(c => c is DefaultValueConstraint),
                prop.Constraints.Any(c => c is UniqueConstraint),
                prop.Constraints.OfType<LengthConstraint>().FirstOrDefault()?.MaxLength,
                columnName: colName);

            // P2: Apply convention chain
            foreach (var conv in _conventions) {
                var projected = conv.ProjectColumn(prop, column);
                if (projected is not null)
                    column = projected;
            }

            columns.Add(column);
        }

        foreach (var rel in _relationships) {
            if (!string.Equals(rel.Source.TypeName, entity.Name, StringComparison.Ordinal)) continue;
            var isCollection = rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;
            var nav = new StorageNavigation(rel, DomainTypeMapping.ToPascalCase(rel.Name), isCollection);
            if (isCollection) collections.Add(nav);
            else references.Add(nav);
        }

        return (columns, collections, references);
    }

    private List<StorageForeignKey> BuildForeignKeys(
        Entity entity,
        AggregateEntity? agg,
        Dictionary<string, AggregateEntity>? aggLookup) {
        var fks = new List<StorageForeignKey>();
        if (agg is null || agg.IsRoot || agg.AggregateParentName is null)
            return fks;

        var parentKeyProperty = "Id";
        if (aggLookup is not null &&
            aggLookup.TryGetValue(agg.AggregateParentName, out _) &&
            _entityLookup.TryGetValue(agg.AggregateParentName, out var parentEntity)) {
            var parentMeta = _analysis?.GetMetadata<EntityStructureMetadata>(parentEntity);
            if (parentMeta?.KeyPropertyName is not null)
                parentKeyProperty = parentMeta.KeyPropertyName;
            else {
                var unique = parentEntity.Properties.FirstOrDefault(p =>
                    p.Constraints.Any(c => c is UniqueConstraint));
                parentKeyProperty = unique?.Name ?? "Id";
            }
        }

        var childPropertyName = agg.BackReferencePropertyName is not null
            ? DomainTypeMapping.ToPascalCase(agg.BackReferencePropertyName) + "Id"
            : DomainTypeMapping.ToPascalCase(agg.AggregateParentName) + "Id";

        fks.Add(new StorageForeignKey(
            childPropertyName,
            agg.AggregateParentName,
            parentKeyProperty));

        return fks;
    }

    private static List<StorageSubscriptionList> DetectSubscriptionLists(
        string entityName,
        List<StorageNavigation> collectionNavs,
        EffectTopology? topo) {
        var lists = new List<StorageSubscriptionList>();
        if (topo is null) return lists;

        var subsBySubscriber = topo.Subscriptions
            .Where(s => string.Equals(s.SubscriberEntity, entityName, StringComparison.Ordinal))
            .GroupBy(s => s.RelationshipName, StringComparer.Ordinal);

        foreach (var group in subsBySubscriber) {
            var nav = collectionNavs
                .FirstOrDefault(n => string.Equals(
                    n.PropertyName,
                    DomainTypeMapping.ToPascalCase(group.Key),
                    StringComparison.Ordinal));
            if (nav is null) continue;

            var events = group.Select(s => s.TargetStage).Distinct().ToList();
            lists.Add(new StorageSubscriptionList(
                DomainTypeMapping.ToPascalCase(group.Key), entityName, events));
        }

        return lists;
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