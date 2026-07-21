using Poly.DomainModeling.Constraints;

namespace Poly.DomainModeling.Lowering;

// ═══════════════════════════════════════════════════════════════
// Storage model — persistence concerns
//
// Everything about how domain data gets stored and fetched:
// aggregate boundaries, keys, columns, navigations, foreign keys,
// soft-delete, stage tracking, and subscription-list shape.
//
// Storage-agnostic codegen backends (EF, Orleans storage, Dapper,
// etc.) consume this to map domain concepts to their own
// storage conventions.
// ═══════════════════════════════════════════════════════════════

/// <summary>Top-level storage model for a domain.</summary>
public sealed record StorageModel(
    string DomainName,
    IReadOnlyList<StorageEntity> Entities,
    IReadOnlyList<StorageRelationship> Relationships
);

/// <summary>
/// Storage-level view of an entity — how it maps to a persistent store.
/// </summary>
public sealed record StorageEntity {
    public StorageEntity(Entity source) {
        Source = source;
    }

    /// <summary>The source domain entity.</summary>
    public Entity Source { get; }

    /// <summary>Entity name (also the table/collection name basis).</summary>
    public string Name => Source.Name;

    /// <summary>Pluralized name for table/DbSet naming.</summary>
    public string TableName => Name + "s";

    // ── Aggregate hierarchy ──────────────────────────────────

    /// <summary>
    /// True if this entity can exist independently (no required entity-ref
    /// constructor params). Root entities form the top of aggregate hierarchies.
    /// </summary>
    public bool IsRoot { get; set; }

    /// <summary>For child entities: the aggregate root entity that owns this one.</summary>
    public string? AggregateParentName { get; set; }

    /// <summary>For child entities: the relationship from parent to this entity.</summary>
    public string? ParentRelationshipName { get; set; }

    /// <summary>
    /// For child entities: the back-reference property name on this entity
    /// pointing to the parent (e.g. Loan.borrower → Patron).
    /// </summary>
    public string? BackReferencePropertyName { get; set; }

    /// <summary>The parent entity resolved from AggregateParentName.</summary>
    public StorageEntity? AggregateParent { get; set; }

    // ── Key structure ─────────────────────────────────────────

    /// <summary>The property used as the natural key (null = shadow key).</summary>
    public Property? KeyProperty { get; set; }

    /// <summary>Key parameter name: key property name or "id".</summary>
    public string KeyName { get; set; } = "id";

    /// <summary>CLR type for the key: "string" for natural keys, "int" for shadow.</summary>
    public string KeyClrType { get; set; } = "int";

    /// <summary>True when the entity has no natural key and uses a shadow key.</summary>
    public bool HasShadowKey => KeyProperty is null;

    // ── Columns ───────────────────────────────────────────────

    /// <summary>Properties that map to database columns (not navigations or FKs).</summary>
    public IReadOnlyList<StorageColumn> Columns => _columns;
    private readonly List<StorageColumn> _columns = new();

    /// <summary>Collection navigation properties (one-to-many from this entity).</summary>
    public IReadOnlyList<StorageNavigation> CollectionNavigations => _collectionNavs;
    private readonly List<StorageNavigation> _collectionNavs = new();

    /// <summary>Singular navigation properties (one-to-one references from this entity).</summary>
    public IReadOnlyList<StorageNavigation> ReferenceNavigations => _refNavs;
    private readonly List<StorageNavigation> _refNavs = new();

    /// <summary>Foreign keys where this entity's table references a parent.</summary>
    public IReadOnlyList<StorageForeignKey> ForeignKeys => _foreignKeys;
    private readonly List<StorageForeignKey> _foreignKeys = new();

    // ── Soft delete ───────────────────────────────────────────

    /// <summary>True if this entity has an IsDeleted property (soft-delete).</summary>
    public bool HasSoftDelete { get; set; }

    // ── Stage tracking ────────────────────────────────────────

    /// <summary>True if this entity has lifecycle stages.</summary>
    public bool HasStages { get; set; }

    /// <summary>Property name for the stage column (e.g. "CurrentStage").</summary>
    public string? StagePropertyName { get; set; }

    /// <summary>The stage enum type name (e.g. "PatronStage").</summary>
    public string? StageEnumTypeName { get; set; }

    // ── Subscription lists ────────────────────────────────────

    /// <summary>
    /// Backing-field subscriber lists for when-subscriptions.
    /// Each represents an internal list that stores subscriber references
    /// for notification dispatch.
    /// </summary>
    public IReadOnlyList<StorageSubscriptionList> SubscriptionLists => _subLists;
    private readonly List<StorageSubscriptionList> _subLists = new();

    // ── Mutators ──────────────────────────────────────────────

    public void AddColumn(StorageColumn col) => _columns.Add(col);
    public void AddCollectionNavigation(StorageNavigation nav) => _collectionNavs.Add(nav);
    public void AddReferenceNavigation(StorageNavigation nav) => _refNavs.Add(nav);
    public void AddForeignKey(StorageForeignKey fk) => _foreignKeys.Add(fk);
    public void AddSubscriptionList(StorageSubscriptionList sl) => _subLists.Add(sl);
}

/// <summary>Storage-level view of a property — column type, CLR type, constraints.</summary>
public sealed record StorageColumn {
    public StorageColumn(Property source, string columnType) {
        Source = source;
        ColumnType = columnType;
    }

    /// <summary>The source domain property.</summary>
    public Property Source { get; }

    /// <summary>Property name.</summary>
    public string Name => Source.Name;

    /// <summary>Domain type name (Text, Number, Boolean, etc.).</summary>
    public string DomainType => Source.Type.TypeName;

    /// <summary>CLR type name for codegen.</summary>
    public string ClrTypeName { get; set; } = "string";

    /// <summary>Database column type (nvarchar, bigint, etc.).</summary>
    public string ColumnType { get; set; }

    /// <summary>Maximum length for string columns.</summary>
    public int? MaxLength { get; set; }

    /// <summary>True if this column is required (NOT NULL).</summary>
    public bool IsRequired { get; set; }

    /// <summary>True for enum-domain properties needing mapping.</summary>
    public bool IsEnum { get; set; }

    /// <summary>True if this property has a default value constraint.</summary>
    public bool HasDefault { get; set; }

    /// <summary>True if this property is unique (candidate key).</summary>
    public bool IsUnique { get; set; }

    /// <summary>Domain constraints for reference (RawConstraint instances).</summary>
    public IReadOnlyList<Constraint> Constraints => Source.Constraints;
}

/// <summary>
/// Storage-level view of a navigation property (relationship).
/// </summary>
public sealed record StorageNavigation {
    public StorageNavigation(Relationship source, string propertyName, bool isCollection) {
        Source = source;
        PropertyName = propertyName;
        IsCollection = isCollection;
    }

    /// <summary>The source domain relationship.</summary>
    public Relationship Source { get; }

    /// <summary>The property name on the entity (PascalCase).</summary>
    public string PropertyName { get; }

    /// <summary>Target entity name.</summary>
    public string TargetEntityName => Source.Target.TypeName;

    /// <summary>True if this is a collection navigation (one-to-many).</summary>
    public bool IsCollection { get; }

    /// <summary>True if the source entity owns the target (cascade delete).</summary>
    public bool SourceOwnsTarget => Source.SourceOwnsTarget;

    /// <summary>Inverse navigation property name, if known.</summary>
    public string? InversePropertyName { get; set; }
}

/// <summary>Describes a foreign key for mapping.</summary>
public sealed record StorageForeignKey(
    string ChildPropertyName,
    string ParentEntityName,
    string ParentKeyProperty
);

/// <summary>Describes a subscriber-list backing field for when-subscriptions.</summary>
public sealed record StorageSubscriptionList(
    /// <summary>Navigation property this list tracks (e.g. "Loans").</summary>
    string NavigationName,
    /// <summary>The subscriber entity type (e.g. "Patron").</summary>
    string SubscriberEntity,
    /// <summary>Event/stage names this subscription listens for.</summary>
    IReadOnlyList<string> EventNames
);

/// <summary>Storage-level view of a relationship.</summary>
public sealed record StorageRelationship {
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