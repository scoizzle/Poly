namespace Poly.DomainModeling.Lowering;

// ═══════════════════════════════════════════════════════════════
// Transport model — protocol concerns
//
// Everything about how domain data is served over protocols:
// action surface (parameters, return types, policies, stage
// transitions), cross-entity topology (create-in, invoke,
// subscriptions), and visibility rules.
//
// Protocol-agnostic codegen backends (REST, GraphQL, gRPC, etc.)
// consume this to map domain concepts to their own protocol
// conventions without re-deriving from raw domain types.
// ═══════════════════════════════════════════════════════════════

/// <summary>Top-level transport model for a domain.</summary>
public sealed record TransportModel(
    string DomainName,
    IReadOnlyList<TransportEntity> Entities,
    EffectTopology Effects
);

/// <summary>Transport-level view of an entity — action surface and routing context.</summary>
public sealed record TransportEntity {
    public TransportEntity(string name) {
        Name = name;
    }

    /// <summary>Entity name.</summary>
    public string Name { get; }

    /// <summary>
    /// The aggregate root that provides routing context for this entity.
    /// Null for root entities, set for children (same as StorageEntity.AggregateParentName).
    /// </summary>
    public string? TransportParentName { get; set; }

    /// <summary>All exposed actions (entity-level + stage-scoped).</summary>
    public IReadOnlyList<TransportAction> Actions => _actions;
    private readonly List<TransportAction> _actions = new();

    public void AddAction(TransportAction a) => _actions.Add(a);
}

/// <summary>
/// Transport-level view of an action — parameter shape, return type,
/// policy guards, and stage transitions that define its protocol surface.
/// </summary>
public sealed record TransportAction {
    public TransportAction(
        string entityName,
        string? stageName,
        string name,
        IReadOnlyList<TransportParameter> parameters,
        bool isVoid,
        string? resultTypeName,
        IReadOnlyList<string> requiredPolicies,
        IReadOnlyList<StageTransitionTarget> stageTransitions
    ) {
        EntityName = entityName;
        StageName = stageName;
        Name = name;
        Parameters = parameters;
        IsVoid = isVoid;
        ResultTypeName = resultTypeName;
        RequiredPolicies = requiredPolicies;
        StageTransitions = stageTransitions;
    }

    /// <summary>The entity this action belongs to.</summary>
    public string EntityName { get; }

    /// <summary>
    /// Non-null when this action is scoped to a specific lifecycle stage.
    /// Null for entity-level actions (available in any stage).
    /// </summary>
    public string? StageName { get; }

    /// <summary>Action name (PascalCase in the domain).</summary>
    public string Name { get; }

    /// <summary>Action parameters with CLR type and entity-ref classification.</summary>
    public IReadOnlyList<TransportParameter> Parameters { get; }

    /// <summary>True when the action has no return value (void).</summary>
    public bool IsVoid { get; }

    /// <summary>
    /// The result type name when non-void.
    /// E.g. for <c>action Checkout(…) -> Loan { … }</c>, this is <c>"Loan"</c>.
    /// </summary>
    public string? ResultTypeName { get; }

    /// <summary>Names of policies that guard this action.</summary>
    public IReadOnlyList<string> RequiredPolicies { get; }

    /// <summary>
    /// Stage transitions caused by this action (from StageTransitionEffect
    /// within its effect body). Typically 0 or 1 entry.
    /// </summary>
    public IReadOnlyList<StageTransitionTarget> StageTransitions { get; }
}

/// <summary>Parameter metadata for a transport action.</summary>
public sealed record TransportParameter(
    string Name,
    string DomainType,
    string ClrTypeName,
    bool IsRequired,
    bool IsEntityRef
);

/// <summary>Target stage for a stage-transition effect within an action body.</summary>
public sealed record StageTransitionTarget(string TargetStageName);

// ═══════════════════════════════════════════════════════════════
// Effect topology — cross-entity effect relationships
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Topology of cross-entity effects — which actions create entities in
/// other aggregates, which invoke actions on other entities, and which
/// subscribe to stage transitions on related entities.
///
/// These patterns define the service-to-service coupling surface
/// that protocol backends must account for (e.g. create-in as
/// nested creation, invoke as remote procedure, subscriptions as
/// event-driven callbacks).
/// </summary>
public sealed record EffectTopology(
    /// <summary>Which entities create which others, and via which relationship.</summary>
    IReadOnlyList<CreateInRelation> CreateInRelations,

    /// <summary>Which actions call actions on other entities.</summary>
    IReadOnlyList<CrossEntityInvoke> CrossEntityInvokes,

    /// <summary>Which entities subscribe to which other entities' stage transitions.</summary>
    IReadOnlyList<SubscriptionRelation> Subscriptions
);

/// <summary>A <c>create in RelName { ... }</c> effect within an action body.</summary>
public sealed record CreateInRelation(
    string CreatorEntity,
    string ActionName,
    string RelationshipName,
    string CreatedEntity
);

/// <summary>An <c>invoke Rel.Action</c> effect from one entity to another.</summary>
public sealed record CrossEntityInvoke(
    string SourceEntity,
    string ActionName,
    string? TargetRelationship,
    string TargetAction
);

/// <summary>A <c>when RelName TargetStage { effects }</c> subscription.</summary>
public sealed record SubscriptionRelation(
    string SubscriberEntity,
    string RelationshipName,
    string TargetStage
);