namespace Poly.DomainModeling.Analysis;

// ═══════════════════════════════════════════════════════════════
// Topology model — cross-entity effect relationships
//
// Derived domain fact: describes how entities couple across
// aggregate boundaries through their effect trees.
//
// Shared by aggregate analysis (who creates whom), storage
// (subscription list backing fields), and transport
// (nested creation, cross-entity invokes, subscriptions).
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Topology of cross-entity effects — which actions create entities in
/// other aggregates, which invoke actions on other entities, and which
/// subscribe to stage transitions on related entities.
/// </summary>
public sealed record EffectTopology(
    /// <summary>Which entities create which others, and via which relationship.</summary>
    IReadOnlyList<CreateInRelation> CreateInRelations,

    /// <summary>Which actions call actions on other entities.</summary>
    IReadOnlyList<CrossEntityInvoke> CrossEntityInvokes,

    /// <summary>Which entities subscribe to which other entities' stage transitions.</summary>
    IReadOnlyList<SubscriptionRelation> Subscriptions
);

/// <summary>
/// A <c>create in RelName { ... }</c> effect within an action body.
/// <paramref name="StageName"/> is the owning stage when the action is
/// stage-scoped, otherwise <c>null</c> (entity-level action).
/// </summary>
public sealed record CreateInRelation(
    string CreatorEntity,
    string ActionName,
    string RelationshipName,
    string CreatedEntity,
    string? StageName = null
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