using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

// ═══════════════════════════════════════════════════════════════
// Storage mapping — persistence conventions applied to domain facts
//
// This is a *convention-specific view* — it consumes the shared
// derived facts (EntityStructureMetadata, AggregateModel) and adds
// storage-layer decisions: column types, table names, foreign key
// naming, navigation field access mode, subscription list backing
// fields, and soft-delete/stage tracking storage shape.
// ═══════════════════════════════════════════════════════════════

/// <summary>Top-level storage mapping for a domain.</summary>
public sealed record StorageModel(
    string DomainName,
    IReadOnlyList<StorageEntity> Entities,
    IReadOnlyList<StorageRelationship> Relationships
);

/// <summary>
/// Storage-level view of an entity — how it maps to a persistent store.
/// Lifetime is tied to the source domain graph via <see cref="Source"/>.
/// </summary>
public sealed class StorageEntity {
    public StorageEntity(
        Entity source,
        string keyName,
        string keyClrType,
        Property? keyProperty,
        bool isRoot,
        string? aggregateParentName,
        bool hasStages,
        string? stagePropertyName,
        string? stageEnumTypeName,
        IReadOnlyList<StorageColumn> columns,
        IReadOnlyList<StorageNavigation> collectionNavigations,
        IReadOnlyList<StorageNavigation> referenceNavigations,
        IReadOnlyList<StorageForeignKey> foreignKeys,
        IReadOnlyList<StorageSubscriptionList> subscriptionLists,
        string? tableName = null,
        StorageIndex? keyIndex = null) {
        Source = source;
        KeyIndex = keyIndex;
        RouteKeys = keyIndex is null
            ? []
            : keyIndex.Columns.Select(c => DomainTypeMapping.ToCamelCase(c.Name)).ToList();
        KeyName = keyName;
        KeyClrType = keyClrType;
        KeyProperty = keyProperty;
        IsRoot = isRoot;
        AggregateParentName = aggregateParentName;
        HasStages = hasStages;
        StagePropertyName = stagePropertyName;
        StageEnumTypeName = stageEnumTypeName;
        Columns = columns;
        CollectionNavigations = collectionNavigations;
        ReferenceNavigations = referenceNavigations;
        ForeignKeys = foreignKeys;
        SubscriptionLists = subscriptionLists;
        if (tableName is not null && string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name must be non-empty when provided.", nameof(tableName));
        TableName = tableName ?? Name + "s";
    }

    /// <summary>The source domain entity.</summary>
    public Entity Source { get; }

    /// <summary>Entity name (also the table/collection name basis).</summary>
    public string Name => Source.Name;

    /// <summary>Physical table name. Defaults to <c>Name + "s"</c>; overridable via <c>table("…")</c>.</summary>
    public string TableName { get; init; }

    /// <summary>
    /// The unique index over the entity's declared <c>key(...)</c> designation (#405), in key
    /// order. Null for a keyless entity, and for a key that names a navigation (its foreign-key
    /// shape is not decided yet). Independent of <see cref="KeyProperty"/>, the pre-#405 key.
    /// </summary>
    public StorageIndex? KeyIndex { get; }

    /// <summary>
    /// Route-key segment names (camel-cased, in key order) derived from the declared
    /// <c>key(...)</c> (#405). Empty unless a key is declared and has a <see cref="KeyIndex"/>; a
    /// <c>unique</c> property alone yields none. The legacy <see cref="KeyName"/> is unchanged.
    /// </summary>
    public IReadOnlyList<string> RouteKeys { get; }

    /// <summary>The property used as the natural key (null = shadow key).</summary>
    public Property? KeyProperty { get; }

    /// <summary>Key parameter name: key property name or "id".</summary>
    public string KeyName { get; }

    /// <summary>CLR type for the key: natural key CLR type, or "int" for shadow.</summary>
    public string KeyClrType { get; }

    /// <summary>True when the entity has no natural key and uses a shadow key.</summary>
    public bool HasShadowKey => KeyProperty is null;

    /// <summary>True if this entity is an aggregate root.</summary>
    public bool IsRoot { get; }

    /// <summary>For child entities: the aggregate root that owns this one.</summary>
    public string? AggregateParentName { get; }

    /// <summary>Properties that map to database columns (not navigations or FKs).</summary>
    public IReadOnlyList<StorageColumn> Columns { get; }

    /// <summary>Collection navigation properties (one-to-many from this entity).</summary>
    public IReadOnlyList<StorageNavigation> CollectionNavigations { get; }

    /// <summary>Singular navigation properties (one-to-one references from this entity).</summary>
    public IReadOnlyList<StorageNavigation> ReferenceNavigations { get; }

    /// <summary>Foreign keys where this entity's table references a parent.</summary>
    public IReadOnlyList<StorageForeignKey> ForeignKeys { get; }

    /// <summary>True if this entity has lifecycle stages.</summary>
    public bool HasStages { get; }

    /// <summary>Property name for the stage column (e.g. "CurrentStage").</summary>
    public string? StagePropertyName { get; }

    /// <summary>The stage enum type name (e.g. "PatronStage").</summary>
    public string? StageEnumTypeName { get; }

    /// <summary>
    /// Backing-field subscriber lists for when-subscriptions.
    /// </summary>
    public IReadOnlyList<StorageSubscriptionList> SubscriptionLists { get; }
}

/// <summary>Storage-level view of a property — column name, type, CLR type, constraints.</summary>
public sealed class StorageColumn {
    public StorageColumn(
        Property source,
        string columnType,
        string clrTypeName,
        bool isEnum,
        bool isRequired,
        bool hasDefault,
        bool isUnique,
        int? maxLength,
        string? columnName = null,
        ValueRange? verifiedRange = null,
        bool isRangeVerified = false) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnType);
        ArgumentException.ThrowIfNullOrWhiteSpace(clrTypeName);
        if (columnName is not null && string.IsNullOrWhiteSpace(columnName))
            throw new ArgumentException("Column name must be non-empty when provided.", nameof(columnName));

        Source = source;
        ColumnName = columnName ?? DomainTypeMapping.ToCamelCase(Source.Name);
        ColumnType = columnType;
        ClrTypeName = clrTypeName;
        IsEnum = isEnum;
        IsRequired = isRequired;
        HasDefault = hasDefault;
        IsUnique = isUnique;
        MaxLength = maxLength;
        VerifiedRange = verifiedRange;
        IsRangeVerified = isRangeVerified;
    }

    public Property Source { get; }

    /// <summary>The domain property name (PascalCase).</summary>
    public string Name => Source.Name;

    /// <summary>The physical column name (camelCase by default, overridable via <c>column("NAME")</c>).</summary>
    public string ColumnName { get; init; }

    public string DomainType => Source.Type.TypeName;
    public string ClrTypeName { get; init; }
    public string ColumnType { get; init; }
    public int? MaxLength { get; init; }

    /// <summary>The analysis-verified value envelope for the column: non-null only when the
    /// invariant analysis proved no effect can produce a value outside the property's declared
    /// range. Emitting a DB CHECK from this is sound (no false-positive constraint).</summary>
    public ValueRange? VerifiedRange { get; init; }

    /// <summary>True when the invariant analysis verified all writers stay within the declared
    /// range for this property (no postcondition violation was found).</summary>
    public bool IsRangeVerified { get; init; }
    public bool IsRequired { get; init; }
    public bool IsEnum { get; init; }
    public bool HasDefault { get; init; }
    public bool IsUnique { get; init; }
    public IReadOnlyList<Constraint> Constraints => Source.Constraints;
}

/// <summary>What the database does to a dependent row when its principal row is deleted.</summary>
public enum StorageDeleteBehavior {
    /// <summary>The optional-link rule, and what every foreign key had before L-15b: the dependent's key is
    /// nulled by the application (EF's <c>ClientSetNull</c>); the database declares no action.</summary>
    ClientSetNull,

    /// <summary>An owned part goes with its owner.</summary>
    Cascade,

    /// <summary>Any other required link: the principal cannot be deleted while a dependent references it.</summary>
    Restrict,
}

/// <summary>Storage-level view of a navigation property (relationship).</summary>
public sealed class StorageNavigation {
    public StorageNavigation(
        Relationship source,
        string propertyName,
        bool isCollection,
        string? inversePropertyName = null,
        bool inverseIsCollection = false,
        bool isRequired = false,
        StorageDeleteBehavior? onDelete = null,
        bool configuresForeignKey = false) {
        Source = source;
        PropertyName = propertyName;
        IsCollection = isCollection;
        InversePropertyName = inversePropertyName;
        InverseIsCollection = inverseIsCollection;
        IsRequired = isRequired;
        OnDelete = onDelete;
        ConfiguresForeignKey = configuresForeignKey;
    }

    /// <summary>The foreign key of the relationship this navigation belongs to is required. It is the published
    /// <c>RelationshipContract.IsRequired</c> of the dependent end's navigation: a collection is never required.</summary>
    public bool IsRequired { get; }

    /// <summary>The delete behaviour of that foreign key, or null when the shape does not decide one
    /// (a many-to-many): the generator then keeps its old output for the navigation.</summary>
    public StorageDeleteBehavior? OnDelete { get; }

    /// <summary>True on exactly one navigation of a decided relationship: the one the foreign key is configured from
    /// (the dependent end's reference navigation, else the collection navigation of a one-to-many with no reference end).</summary>
    public bool ConfiguresForeignKey { get; }

    public Relationship Source { get; }
    public string PropertyName { get; }
    public string TargetEntityName => Source.Target.TypeName;
    public bool IsCollection { get; }
    public bool SourceOwnsTarget => Source.SourceOwnsTarget;
    public string? InversePropertyName { get; }

    /// <summary>True when the resolved inverse is a collection navigation; false when there is no inverse.</summary>
    public bool InverseIsCollection { get; }
}

/// <summary>A unique index over columns, in order. Columns are the storage view of the key members.</summary>
public sealed record StorageIndex(IReadOnlyList<StorageColumn> Columns, bool IsUnique);

/// <summary>Describes a foreign key for mapping.</summary>
public sealed record StorageForeignKey(
    string ChildPropertyName,
    string ParentEntityName,
    string ParentKeyProperty
);

/// <summary>Describes a subscriber-list backing field for when-subscriptions.</summary>
public sealed record StorageSubscriptionList(
    string NavigationName,
    string SubscriberEntity,
    IReadOnlyList<string> EventNames
);

/// <summary>Storage-level view of a relationship.</summary>
public sealed class StorageRelationship {
    public StorageRelationship(Relationship source) {
        Source = source;
    }

    public Relationship Source { get; }
    public string Name => Source.Name;
    public string SourceType => Source.Source.TypeName;
    public string TargetType => Source.Target.TypeName;
    public RelationshipCardinality Cardinality => Source.Cardinality;
    public bool SourceOwnsTarget => Source.SourceOwnsTarget;
}