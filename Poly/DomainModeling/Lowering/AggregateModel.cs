namespace Poly.DomainModeling.Lowering;

// ═══════════════════════════════════════════════════════════════
// Aggregate model — ownership hierarchy
//
// A derived domain fact expressing parent/child ownership
// relationships. Every child entity has exactly one aggregate
// root (parent). This is shared by both storage (cascade
// deletes, constraint ordering) and transport (URL nesting,
// lifecycle scoping, GraphQL hierarchy).
//
// Distinguished from EntityStructureMetadata (which is
// entity-local) by being inherently cross-entity and depending
// on relationship topology plus create-in effect analysis.
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Cross-entity aggregate ownership — which entities own which.
///
/// This is a derived domain fact, not a storage or transport
/// convention. Both StorageModel and TransportSurface consume it.
/// </summary>
public sealed record AggregateModel(
    string DomainName,
    IReadOnlyList<AggregateEntity> Entities
);

/// <summary>
/// Aggregate-level view of an entity — its place in the
/// ownership hierarchy.
/// </summary>
public sealed class AggregateEntity {
    public AggregateEntity(
        string name,
        bool isRoot,
        string? aggregateParentName = null,
        string? parentRelationshipName = null,
        string? backReferencePropertyName = null,
        AggregateEntity? aggregateParent = null) {
        Name = name;
        IsRoot = isRoot;
        AggregateParentName = aggregateParentName;
        ParentRelationshipName = parentRelationshipName;
        BackReferencePropertyName = backReferencePropertyName;
        AggregateParent = aggregateParent;
    }

    /// <summary>Entity name.</summary>
    public string Name { get; }

    /// <summary>
    /// True if this entity is an aggregate root (no required
    /// entity-reference constructor params).
    /// </summary>
    public bool IsRoot { get; }

    /// <summary>For child entities: the root that owns this one.</summary>
    public string? AggregateParentName { get; }

    /// <summary>For child entities: the relationship from parent to child.</summary>
    public string? ParentRelationshipName { get; }

    /// <summary>
    /// For child entities: the singular navigation property on
    /// this entity that points back to the parent (e.g. Loan.borrower).
    /// </summary>
    public string? BackReferencePropertyName { get; }

    /// <summary>Resolved parent entity reference.</summary>
    public AggregateEntity? AggregateParent { get; }
}