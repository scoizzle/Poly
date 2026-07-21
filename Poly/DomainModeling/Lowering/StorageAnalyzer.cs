using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Analyzes a <see cref="Domain"/> and produces a <see cref="StorageModel"/>
/// — aggregate boundaries, keys, columns, navigations, foreign keys,
/// soft-delete, stage tracking, and subscription-list shape.
///
/// Call <see cref="Analyze"/> to compute the storage model, or use
/// <see cref="InfrastructureAnalyzer"/> which coordinates both
/// storage and transport analysis.
///
/// When an <see cref="AnalysisResult"/> is available (from domain evolution),
/// pass it to leverage pre-computed <see cref="DomainTypeLookupMetadata"/>
/// and other metadata from the domain analysis pipeline.
/// </summary>
public sealed class StorageAnalyzer {
    private readonly Domain _domain;
    private readonly List<Entity> _entities;
    private readonly List<Relationship> _relationships;
    private readonly Dictionary<string, Entity> _entityLookup;
    private readonly Dictionary<string, List<Relationship>> _incomingRels;
    private readonly AnalysisResult? _analysis;

    public StorageAnalyzer(Domain domain, AnalysisResult? analysis = null) {
        _domain = domain;
        _analysis = analysis;

        // Use pre-computed metadata from analysis pipeline when available
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

    public StorageModel Analyze(EffectTopology? topology = null) {
        var storageEntities = new List<StorageEntity>();

        foreach (var entity in _entities) {
            storageEntities.Add(AnalyzeEntity(entity));
        }

        // Pass 2: resolve aggregate parents (need all entities analyzed first)
        var storageLookup = storageEntities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        foreach (var store in storageEntities) {
            ResolveParent(store, storageLookup, topology);
        }

        // Pass 3: detect subscription lists (need parent resolution done)
        foreach (var store in storageEntities) {
            DetectSubscriptionLists(store, topology);
        }

        var rels = _relationships.Select(r => new StorageRelationship(r)).ToList();
        return new StorageModel(_domain.Name, storageEntities, rels);
    }

    // ── Per-entity analysis ───────────────────────────────────

    private StorageEntity AnalyzeEntity(Entity entity) {
        var store = new StorageEntity(entity);

        // Use pre-computed metadata from analysis pipeline when available
        var meta = _analysis?.GetMetadata<EntityStructureMetadata>(entity);

        if (meta is not null) {
            // Key structure
            store.KeyProperty = meta.KeyPropertyName is not null
                ? entity.Properties.FirstOrDefault(p =>
                    string.Equals(p.Name, meta.KeyPropertyName, StringComparison.Ordinal))
                : null;
            store.KeyName = meta.KeyPropertyName is not null
                ? ToCamelCase(meta.KeyPropertyName) : "id";
            store.KeyClrType = meta.KeyClrType;

            // Root / aggregate
            store.IsRoot = meta.IsRoot;

            // Soft delete
            store.HasSoftDelete = meta.HasSoftDelete;

            // Stage tracking
            if (meta.HasStages) {
                store.HasStages = true;
                store.StagePropertyName = "CurrentStage";
                store.StageEnumTypeName = meta.StageEnumTypeName;
            }
        }
        else {
            // Fallback: derive from raw domain types (pre-analysis compat)
            AnalyzeKey(entity, store);
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

        // Column vs navigation classification
        ClassifyProperties(entity, store);

        return store;
    }

    private void AnalyzeKey(Entity entity, StorageEntity store) {
        var uniqueProp = entity.Properties.FirstOrDefault(p =>
            p.Constraints.Any(c => c is UniqueConstraint));
        store.KeyProperty = uniqueProp;
        store.KeyName = uniqueProp is not null ? ToCamelCase(uniqueProp.Name) : "id";
        store.KeyClrType = uniqueProp is not null ? "string" : "int";
    }

    private void ResolveParent(StorageEntity store, Dictionary<string, StorageEntity> storageLookup, EffectTopology? topo) {
        if (store.IsRoot) return;

        if (!_incomingRels.TryGetValue(store.Name, out var incoming)) return;

        // Build create-in set for parent priority
        var createInRelNames = topo?.CreateInRelations
            .Where(c => string.Equals(c.CreatedEntity, store.Name, StringComparison.Ordinal))
            .Select(c => c.RelationshipName)
            .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>();

        StorageEntity? chosenParent = null;
        string? chosenRelName = null;
        Relationship? chosenBackRef = null;

        foreach (var rel in incoming) {
            var isCollection = rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;
            if (!isCollection) continue;

            var parentEntity = _entityLookup.GetValueOrDefault(rel.Source.TypeName);
            if (parentEntity is null) continue;

            var parentStore = storageLookup.GetValueOrDefault(parentEntity.Name);
            if (parentStore is null || !parentStore.IsRoot) continue;

            var backRef = _relationships.FirstOrDefault(r =>
                string.Equals(r.Source.TypeName, store.Name, StringComparison.Ordinal) &&
                string.Equals(r.Target.TypeName, parentEntity.Name, StringComparison.Ordinal) &&
                r.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany));

            if (createInRelNames.Contains(rel.Name)) {
                chosenParent = parentStore;
                chosenRelName = rel.Name;
                chosenBackRef = backRef;
                break;
            }

            chosenParent ??= parentStore;
            chosenRelName ??= rel.Name;
            chosenBackRef ??= backRef;
        }

        // Fallback: singular nav parent
        if (chosenParent is null && incoming.Count > 0) {
            var singular = incoming.FirstOrDefault(r =>
                r.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany));
            if (singular is not null && _entityLookup.ContainsKey(singular.Source.TypeName)) {
                chosenParent = storageLookup.GetValueOrDefault(singular.Source.TypeName);
                chosenRelName = singular.Name;
                chosenBackRef = null;
            }
        }

        if (chosenParent is not null) {
            store.AggregateParentName = chosenParent.Name;
            store.AggregateParent = chosenParent;
            store.ParentRelationshipName = chosenRelName;
            store.BackReferencePropertyName = chosenBackRef?.Name;
        }

        // Effect topology fallback for create-in
        if (store.AggregateParentName is null && topo is not null) {
            var createIn = topo.CreateInRelations.FirstOrDefault(c =>
                string.Equals(c.CreatedEntity, store.Name, StringComparison.Ordinal));
            if (createIn is not null) {
                store.AggregateParentName = createIn.CreatorEntity;
                store.AggregateParent = storageLookup.GetValueOrDefault(createIn.CreatorEntity);
                store.ParentRelationshipName = createIn.RelationshipName;
            }
        }
    }

    private void ClassifyProperties(Entity entity, StorageEntity store) {
        var enumTypes = _domain.Types.OfType<EnumType>()
            .ToDictionary(e => e.Name, StringComparer.Ordinal);

        foreach (var prop in entity.Properties) {
            var isEntityRef = _entityLookup.ContainsKey(prop.Type.TypeName);
            var isEnum = enumTypes.ContainsKey(prop.Type.TypeName);

            if (isEntityRef) continue; // skip nav-type properties here

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

        // Navigation analysis
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

        // Group subscriptions by (subscriber entity, relationship name)
        var subsBySubscriber = topo.Subscriptions
            .Where(s => string.Equals(s.SubscriberEntity, store.Name, StringComparison.Ordinal))
            .GroupBy(s => s.RelationshipName, StringComparer.Ordinal);

        foreach (var group in subsBySubscriber) {
            // Find the navigation that matches this subscription's relationship
            var nav = store.CollectionNavigations
                .FirstOrDefault(n => string.Equals(n.PropertyName, ToPascalCase(group.Key), StringComparison.Ordinal));
            if (nav is null) continue;

            // The subscriber list belongs on the *target* entity (the one with the nav),
            // not the subscriber. The subscriber entity registers for notifications
            // from entities in this navigation. The backing field lives on the
            // target of the relationship (e.g. Loan stores _overdueSubscribers for Patron).
            // Actually, the subscription lists are generated as private backing fields
            // on the entity that *fires* the subscription — the target of the relationship.
            //
            // E.g. Patron subscribes to Loan's "when Overdue" → Loan stores _overdueSubscribers.
            // Since we're on the subscriber entity (Patron), we need to find the target entity (Loan)
            // and note it needs registration methods for Patron.
            var targetEntityName = nav.TargetEntityName;
            var targetEntity = _entities.FirstOrDefault(e =>
                string.Equals(e.Name, targetEntityName, StringComparison.Ordinal));
            if (targetEntity is null) continue;

            // Also detect if there's a corresponding registration method pattern
            // in the target entity's codegen. We record this on the *target's* storage entity
            // as a subscription list.
            var events = group.Select(s => s.TargetStage).Distinct().ToList();

            // We're on the subscriber entity — the subscription list lives on the
            // *source* of the navigation (the creator of the child aggregates).
            // Detect existing subscription lists by convention.
            var subList = new StorageSubscriptionList(
                ToPascalCase(group.Key),
                store.Name,
                events
            );
            store.AddSubscriptionList(subList);
        }
    }

    // ── Helpers ───────────────────────────────────────────────

    public bool HasRequiredEntityRef(Entity entity) {
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

    public static string GetColumnType(Property prop, bool isEnum) {
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

    public static string GetClrTypeName(string domainType) => domainType switch {
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