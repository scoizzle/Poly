using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Per-entity structural properties derived from the domain model.
///
/// Pre-computed by <see cref="EntityStructureAnalyzer"/> during the analysis
/// pipeline, so lowering passes like <see cref="Lowering.StorageAnalyzer"/>
/// and <see cref="Lowering.TransportAnalyzer"/> can consume them without
/// re-scanning entity properties and constraints.
/// </summary>
public sealed record EntityStructureMetadata(
    /// <summary>True if this entity can exist independently (root of aggregate).</summary>
    bool IsRoot,

    /// <summary>True when the entity has a <see cref="UniqueConstraint"/> property.</summary>
    bool HasNaturalKey,

    /// <summary>The unique property name (e.g. "ISBN") when <see cref="HasNaturalKey"/> is true.</summary>
    string? KeyPropertyName,

    /// <summary>CLR type string: "string" for natural keys, "int" for shadow keys.</summary>
    string KeyClrType,

    /// <summary>True if this entity has lifecycle stages or a matching stage enum.</summary>
    bool HasStages,

    /// <summary>The stage enum type name (e.g. "PatronStage"), null when <see cref="HasStages"/> is false.</summary>
    string? StageEnumTypeName,

    /// <summary>
    /// Optional stage-name lookup table for this entity. Null when
    /// <see cref="HasStages"/> is false.
    /// </summary>
    IReadOnlyDictionary<string, Stage>? StageByName,

    /// <summary>
    /// Constructor parameter order for entity creation lowering: declaration order
    /// within the required block, then within the trailing defaulted/optional block.
    /// The block split itself is L-08's (it makes every Create parameter optional).
    /// </summary>
    IReadOnlyList<ConstructorParameterOrder> ConstructorParameters,

    /// <summary>
    /// Every entity property that is required at create — the property has no
    /// <see cref="Ontology.Constraints.OptionalConstraint"/>. Required is the default and
    /// <c>optional</c> is its opt-out, so this set is the single requiredness fact:
    /// consumers do not re-scan constraints to decide whether a property may be omitted.
    /// </summary>
    IReadOnlySet<string> RequiredPropertyNames,

    /// <summary>
    /// Map of entity property name → enum type name for properties whose type is
    /// an enum. Published so lowering consumers (exporter, expression pass) resolve
    /// enum-typed literals to qualified members without re-scanning the catalog.
    /// Null when the entity has no enum-typed properties.
    /// </summary>
    IReadOnlyDictionary<string, string>? EnumPropertyNames = null,

    /// <summary>
    /// Names of entity properties assigned by the initial stage's entry effects.
    /// The exported constructor runs those effects after setting CurrentStage, so
    /// these props are body-initialized — never ctor params (a param would be dead
    /// and written twice, e.g. StartedAt). Published so the exporter's ctor emission
    /// and this signature stay in lockstep without re-deriving the rule.
    /// Definitely assigned: top-level <c>OnEntry</c> assigns only. The may-assign create-time set
    /// (conditional, loop, exit, hop, invoked action) is <c>StageEntryFacts.EntryAssignedProperties</c>,
    /// which only fail-closed checks may read (<c>DMEFF019</c>).
    /// </summary>
    IReadOnlySet<string> EntryAssignedPropertyNames = null!,

    /// <summary>
    /// Property name whose type is a domain enum whose member-name set is exactly
    /// equal to the entity's stage-name set — the lifecycle property a transition
    /// keeps in lockstep with <c>CurrentStage</c>. Null when there is no such
    /// property (or the entity has no stage enum). See <see cref="LifecycleEnumTypeName"/>.
    /// </summary>
    string? LifecyclePropertyName = null,

    /// <summary>Enum type name for <see cref="LifecyclePropertyName"/>; null together with it.</summary>
    string? LifecycleEnumTypeName = null,

    /// <summary>Scalar properties assigned only by effects (no create initializer).</summary>
    IReadOnlySet<string>? AssignOnlyPropertyNames = null,

    /// <summary>
    /// Properties whose every writer is proven to leave them within their declared constraints
    /// (#367 C-1 guard-closed fact). Conservative: absent means not proven, never "violated".
    /// Writer kinds and blockers are in <see cref="GuardClosedProperties"/>.
    /// </summary>
    IReadOnlySet<string>? GuardClosedPropertyNames = null,

    /// <summary>
    /// The entity's declared key designation (<see cref="Entity.KeyProperties"/>, #405); empty when the
    /// entity is unkeyed. A fact over existing members: no synthetic member exists for it.
    /// </summary>
    IReadOnlyList<string>? KeyProperties = null
) : IAnalysisMetadata {
    /// <summary>
    /// <c>Id</c> is an alias of the key designation: the single key property, or the tuple of them in
    /// declared order. Empty for an unkeyed entity, which has an instance handle but no domain identity.
    /// </summary>
    public IReadOnlyList<string> Id => KeyProperties ?? [];

    /// <summary>True when the entity designates a key.</summary>
    public bool HasKey => Id.Count > 0;
}

public sealed record ConstructorParameterOrder(
    string Name,
    DomainTypeReference Type,
    bool IsNavigation,
    bool IsBackReference,
    bool IsCollection = false,
    bool IsOptional = false
);