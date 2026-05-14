namespace Poly.DomainModeling.V2;

/// <summary>
/// Immutable read-only snapshot of the full domain model for use by UI, API, and MCP consumers.
/// </summary>
public sealed record DomainModelSnapshot(
    string DomainName,
    IReadOnlyList<PrimitiveSnapshot> Primitives,
    IReadOnlyList<EntitySnapshot> Entities,
    IReadOnlyList<EventTypeSnapshot> EventTypes,
    IReadOnlyList<RelationshipSnapshot> Relationships
);

public sealed record PrimitiveSnapshot(string Name, string Category);

public sealed record PropertySnapshot(string Name, string TypeName);

public sealed record StageSnapshot(string Name, string? ParentStageName, IReadOnlyList<string> ActionNames);

public sealed record ActionSnapshot(
    string Name,
    IReadOnlyList<PropertySnapshot> Parameters,
    IReadOnlyList<string> EffectTypes,
    IReadOnlyList<string> PublishedEventNames,
    IReadOnlyList<string> TransitionTargetNames
);

public sealed record EntitySnapshot(
    string Name,
    string? ParentEntityName,
    IReadOnlyList<PropertySnapshot> Properties,
    IReadOnlyList<StageSnapshot> Stages,
    IReadOnlyList<string> EventNames,
    IReadOnlyList<ActionSnapshot> Actions
);

public sealed record EventTypeSnapshot(
    string Name,
    IReadOnlyList<PropertySnapshot> Properties
);

public sealed record RelationshipSnapshot(
    string Name,
    string SourceEntityName,
    string TargetEntityName,
    string Cardinality,
    bool SourceOwnsTarget
);
