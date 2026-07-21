namespace Poly.DomainModeling.Lowering;

// ═══════════════════════════════════════════════════════════════
// Transport surface — protocol conventions applied to domain facts
//
// This is a *convention-specific view* — it consumes the shared
// derived facts (AggregateModel, BehaviorModel, EffectTopology)
// and adds protocol-level decisions: resource hierarchy (parent
// context), whether an entity is directly exposable, and which
// entities are reachable via which routing context.
//
// Different protocols (REST, GraphQL, gRPC) would consume the
// same shared facts and map them to their own conventions: REST
// maps parent context to URL nesting, GraphQL maps it to field
// resolvers, gRPC maps it to service hierarchy.
// ═══════════════════════════════════════════════════════════════

/// <summary>Top-level transport surface — what's exposable and how it's organized.</summary>
public sealed record TransportSurface(
    string DomainName,
    IReadOnlyList<TransportEntity> Entities,
    EffectTopology Effects
);

/// <summary>
/// Transport-level view of an entity — its routing context within
/// the API surface. Actions are not listed here; they come from
/// <see cref="BehaviorModel"/>.
/// </summary>
public sealed record TransportEntity {
    public TransportEntity(string name) {
        Name = name;
    }

    /// <summary>Entity name.</summary>
    public string Name { get; }

    /// <summary>
    /// The aggregate root that provides routing context for this entity.
    /// Null for root entities; set to the parent for children (same as
    /// AggregateModel.AggregateParentName).
    /// </summary>
    public string? ParentName { get; set; }

    /// <summary>
    /// True if this entity has its own independent lifecycle and can be
    /// directly addressed (GET/POST at top level). False for child
    /// entities nested under parents.
    /// </summary>
    public bool IsExposable { get; set; }
}

// Note: EffectTopology, CreateInRelation, CrossEntityInvoke,
// SubscriptionRelation live in TransportModel.cs alongside
// TransportSurface because they describe the cross-entity coupling
// surface that transport protocols must account for. This may move
// to a standalone file if other consumers need it.