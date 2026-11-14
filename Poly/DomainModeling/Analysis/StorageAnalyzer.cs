using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;

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
    /// <summary>
    /// Domain type name whose pack mapping answers for an enum column: the generated CLR
    /// enum is int-backed, so the column type is the pack's integer type.
    /// </summary>
    private const string EnumColumnBackingDomainType = "Int32";

    private readonly Domain _domain;
    private readonly List<Entity> _entities;
    private readonly List<Relationship> _relationships;
    private readonly Dictionary<string, Entity> _entityLookup;
    private readonly IReadOnlySet<string>? _enumTypeNames;
    private readonly AnalysisContext? _context;
    private readonly AnalysisResult? _analysis;
    private readonly TypeMappingRegistry _typeMaps;
    private readonly IReadOnlyList<IStorageConvention> _conventions;

    public StorageAnalyzer(
        Domain domain,
        AnalysisResult? analysis = null,
        AnalysisContext? context = null,
        TypeMappingRegistry? typeMaps = null,
        IReadOnlyList<IStorageConvention>? conventions = null) {
        _domain = domain;
        _analysis = analysis;
        _context = context;
        _typeMaps = typeMaps ?? new TypeMappingRegistry();
        _conventions = conventions ?? [];

        // Prefer live pipeline bags; fall back to a completed AnalysisResult when
        // StoragePass is constructed standalone with that result.
        var lookup = context?.GetTypeLookup(domain)
            ?? analysis?.GetTypeLookup(domain)
            ?? context?.GetTypeLookup()
            ?? analysis?.GetTypeLookup();
        if (lookup is not null) {
            _entities = lookup.Entities.ToList();
            _entityLookup = lookup.Types
                .Where(kvp => kvp.Value is Entity)
                .ToDictionary(kvp => kvp.Key, kvp => (Entity)kvp.Value, StringComparer.Ordinal);
            // amu-w2-1 / review F4: with analysis present the DTLM is the single
            // source for enum classification too — no domain tree scan.
            _enumTypeNames = lookup.Types
                .Where(kvp => kvp.Value is EnumType)
                .Select(kvp => kvp.Key)
                .ToHashSet(StringComparer.Ordinal);
        }
        else {
            _entities = domain.Types.OfType<Entity>().ToList();
            _entityLookup = _entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
            // Analysis-absent residual (standalone / reduced contract only).
            _enumTypeNames = null;
        }

        _relationships = (context?.GetAllRelationships(domain)
            ?? analysis?.GetAllRelationships(domain)
            ?? domain.Types.OfType<Entity>().SelectMany(e => e.Navigations)).ToList();
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
        // amu-w2-1: context bag first (full pipeline), priorAnalysis fallback (standalone).
        var meta = _context?.GetStructure(entity)
            ?? _analysis?.GetStructure(entity);
        var agg = aggLookup?.GetValueOrDefault(entity.Name);

        Property? keyProperty;
        string keyName;
        string keyClrType;
        bool isRoot;
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
            // The aggregate resolves ownership cross-entity; when it exists it is the
            // truth (a parentless entity is a root there). Structure alone cannot know.
            isRoot = agg?.IsRoot ?? meta.IsRoot;
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
                ? _typeMaps.ToClrTypeName(uniqueProp.Type.TypeName) : "int";
            isRoot = agg?.IsRoot ?? !HasRequiredEntityRef(entity);
            var stageEnumType = _domain.Types.OfType<EnumType>()
                .FirstOrDefault(e => e.Name == $"{entity.Name}Stage");
            if (!EntityStructureAnalyzer.IsImplicitStage(entity, _domain)
                && (stageEnumType is not null || entity.Stages.Count > 0)) {
                hasStages = true;
                stagePropertyName = "CurrentStage";
                stageEnumTypeName = stageEnumType?.Name ?? $"{entity.Name}Stage";
            }
            else {
                hasStages = false;
            }
        }

        var aggregateParentName = agg?.AggregateParentName;
        var verifiedRanges = ComputeVerifiedRanges(entity);
        var (columns, collectionNavs, referenceNavs) = ClassifyProperties(entity, verifiedRanges);
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
            hasStages,
            stagePropertyName,
            stageEnumTypeName,
            columns,
            collectionNavs,
            referenceNavs,
            foreignKeys,
            subscriptionLists,
            tableName,
            BuildKeyIndex(entity, columns));

        // P2: Apply convention chain
        foreach (var conv in _conventions) {
            var projected = conv.ProjectEntity(entity, storageEntity);
            if (projected is not null)
                storageEntity = projected;
        }

        return storageEntity;
    }

    /// <summary>
    /// #405: the declared key as a unique index over its columns, in key order. A keyless entity
    /// yields null; so does a key naming a member with no column (a navigation), whose
    /// foreign-key shape the decision does not state.
    /// </summary>
    private static StorageIndex? BuildKeyIndex(Entity entity, IReadOnlyList<StorageColumn> columns) {
        var key = entity.KeyProperties;
        if (key is null || key.Count == 0)
            return null;
        var keyColumns = new List<StorageColumn>(key.Count);
        foreach (var name in key) {
            var column = columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
            if (column is null)
                return null;
            keyColumns.Add(column);
        }
        return new StorageIndex(keyColumns, IsUnique: true);
    }

    private (List<StorageColumn> Columns, List<StorageNavigation> Collections, List<StorageNavigation> References)
        ClassifyProperties(Entity entity, IReadOnlyDictionary<string, (ValueRange? Range, bool Verified)>? verifiedRanges = null) {
        // amu-w2-1 / review F4: enum classification comes from the DTLM when
        // analysis is present; the tree scan is analysis-absent residual only.
        var enumTypes = _enumTypeNames ?? _domain.Types.OfType<EnumType>()
            .Select(e => e.Name)
            .ToHashSet(StringComparer.Ordinal);
        var columns = new List<StorageColumn>();
        var collections = new List<StorageNavigation>();
        var references = new List<StorageNavigation>();
        // Requiredness is published once by EntityStructureAnalyzer; the tree scan is
        // the analysis-absent fallback for standalone use.
        var requiredPropertyNames = _context?.GetStructure(entity)?.RequiredPropertyNames
            ?? _analysis?.GetStructure(entity)?.RequiredPropertyNames
            ?? EntityStructureAnalyzer.ComputeRequiredPropertyNames(entity);

        foreach (var prop in entity.Properties) {
            var isEntityRef = _entityLookup.ContainsKey(prop.Type.TypeName);
            if (isEntityRef) continue;

            var isEnum = enumTypes.Contains(prop.Type.TypeName);

            // P2: Default column type from registry, then apply column annotation override
            // An enum property's CLR type is the generated (int-backed) enum, so the column
            // type is the pack's mapping for that backing type. Using the enum's own name
            // leaks a type no provider recognises (SQL Server rejects the DDL; SQLite
            // silently accepts it).
            var baseColumnType = isEnum
                ? _typeMaps.ToSqlColumnType(EnumColumnBackingDomainType)
                : _typeMaps.ToSqlColumnType(prop.Type.TypeName);
            var baseClrType = isEnum
                ? prop.Type.TypeName
                : _typeMaps.ToClrTypeName(prop.Type.TypeName);

            var (colName, colType) = ResolveColumnAnnotation(prop);
            var columnType = colType ?? baseColumnType;

            var (verifiedRange, verified) = verifiedRanges is not null && verifiedRanges.TryGetValue(prop.Name, out var vr)
                ? vr
                : (null, false);
            var column = new StorageColumn(
                prop,
                columnType,
                baseClrType,
                isEnum,
                requiredPropertyNames.Contains(prop.Name),
                prop.Constraints.Any(c => c is DefaultValueConstraint),
                prop.Constraints.Any(c => c is UniqueConstraint),
                prop.Constraints.OfType<LengthConstraint>().FirstOrDefault()?.MaxLength,
                columnName: colName,
                verifiedRange: verifiedRange,
                isRangeVerified: verified);

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
            var inverse = ResolveInverse(rel);
            var nav = new StorageNavigation(
                rel,
                DomainTypeMapping.ToPascalCase(rel.Name),
                isCollection,
                inverse is null ? null : DomainTypeMapping.ToPascalCase(inverse.Name),
                inverse?.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany);
            if (isCollection) collections.Add(nav);
            else references.Add(nav);
        }

        return (columns, collections, references);
    }

    /// <summary>
    /// The navigation on the target that pairs with <paramref name="rel"/>, resolved once here for every consumer:
    /// the one it declares (<c>via</c>), else the peer that declares it, else the target's only unclaimed navigation
    /// back (an undeclared pair that is ambiguous is already an error in <c>StructuralDomainAnalyzer</c>).
    /// </summary>
    private Relationship? ResolveInverse(Relationship rel) {
        var peers = _relationships
            .Where(r => !ReferenceEquals(r, rel)
                && string.Equals(r.Source.TypeName, rel.Target.TypeName, StringComparison.Ordinal)
                && string.Equals(r.Target.TypeName, rel.Source.TypeName, StringComparison.Ordinal))
            .ToList();

        if (rel.Inverse is { Length: > 0 } declared)
            return peers.FirstOrDefault(p => string.Equals(p.Name, declared, StringComparison.Ordinal));

        var declaring = peers.Where(p => string.Equals(p.Inverse, rel.Name, StringComparison.Ordinal)).ToList();
        if (declaring.Count > 0)
            return declaring[0];

        var unclaimed = peers.Where(p => p.Inverse is not { Length: > 0 }
            && !_relationships.Any(other => !ReferenceEquals(other, rel)
                && string.Equals(other.Source.TypeName, rel.Source.TypeName, StringComparison.Ordinal)
                && string.Equals(other.Inverse, p.Name, StringComparison.Ordinal))).ToList();
        return unclaimed.Count == 1 ? unclaimed[0] : null;
    }

    private Dictionary<string, (ValueRange? Range, bool Verified)> ComputeVerifiedRanges(Entity entity) =>
        VerifiedRanges.Compute(entity, action =>
            _context?.GetMetadata<ActionInvariantMetadata>(action)
            ?? _analysis?.GetMetadata<ActionInvariantMetadata>(action));

    private List<StorageForeignKey> BuildForeignKeys(Entity entity,
        AggregateEntity? agg,
        Dictionary<string, AggregateEntity>? aggLookup) {
        var fks = new List<StorageForeignKey>();
        if (agg is null || agg.IsRoot || agg.AggregateParentName is null)
            return fks;

        var parentKeyProperty = "Id";
        if (aggLookup is not null &&
            aggLookup.TryGetValue(agg.AggregateParentName, out _) &&
            _entityLookup.TryGetValue(agg.AggregateParentName, out var parentEntity)) {
            var parentMeta = _context?.GetStructure(parentEntity)
                ?? _analysis?.GetStructure(parentEntity);
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
        var required = _context?.GetStructure(entity)?.RequiredPropertyNames
            ?? _analysis?.GetStructure(entity)?.RequiredPropertyNames
            ?? EntityStructureAnalyzer.ComputeRequiredPropertyNames(entity);
        if (entity.Properties.Any(p => required.Contains(p.Name)
            && !p.Constraints.Any(c => c is DefaultValueConstraint)
            && _entityLookup.ContainsKey(p.Type.TypeName)))
            return true;
        // The same fact EntityStructureAnalyzer reads (L-15a): only a required to-one names an owner.
        if (_relationships.Any(r =>
            string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal) &&
            r.IsRequiredToOne() &&
            !string.Equals(r.Target.TypeName, entity.Name, StringComparison.Ordinal)))
            return true;
        return false;
    }
}