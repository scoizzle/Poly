using Poly.Analysis;
using Poly.DomainModeling.Constraints;

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

    /// <summary>True if this entity has an IsDeleted boolean property.</summary>
    bool HasSoftDelete,

    /// <summary>True if this entity has lifecycle stages or a matching stage enum.</summary>
    bool HasStages,

    /// <summary>The stage enum type name (e.g. "PatronStage"), null when <see cref="HasStages"/> is false.</summary>
    string? StageEnumTypeName,

    /// <summary>
    /// Optional stage-name lookup table for this entity. Null when
    /// <see cref="HasStages"/> is false.
    /// </summary>
    IReadOnlyDictionary<string, Stage>? StageByName,

    /// <summary>Constructor parameter order for entity creation lowering.</summary>
    IReadOnlyList<ConstructorParameterOrder> ConstructorParameters,

    /// <summary>
    /// Map of entity property name → enum type name for properties whose type is
    /// an enum. Published so lowering consumers (exporter, expression pass) resolve
    /// enum-typed literals to qualified members without re-scanning the catalog.
    /// Null when the entity has no enum-typed properties.
    /// </summary>
    IReadOnlyDictionary<string, string>? EnumPropertyNames = null,

    /// <summary>
    /// Names of entity properties assigned by the FIRST stage's entry effects.
    /// The exported constructor runs those effects after setting CurrentStage, so
    /// these props are body-initialized — never ctor params (a param would be dead
    /// and written twice, e.g. StartedAt). Published so the exporter's ctor emission
    /// and this signature stay in lockstep without re-deriving the rule.
    /// </summary>
    IReadOnlySet<string> EntryAssignedPropertyNames = null!
) : IAnalysisMetadata;

public sealed record ConstructorParameterOrder(
    string Name,
    DomainTypeReference Type,
    bool IsNavigation,
    bool IsBackReference,
    bool IsCollection = false
);