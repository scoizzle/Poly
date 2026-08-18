using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using PrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;
using Subtract = Poly.DomainModeling.Ontology.Subtract;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

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