namespace Poly.DomainModeling.Analysis;

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
/// convention. StorageModel consumes it.
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

    public string Name { get; }
    public bool IsRoot { get; }
    public string? AggregateParentName { get; }
    public string? ParentRelationshipName { get; }
    public string? BackReferencePropertyName { get; }
    public AggregateEntity? AggregateParent { get; }
}