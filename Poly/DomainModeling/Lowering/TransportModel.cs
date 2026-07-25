using Poly.DomainModeling.Analysis;

namespace Poly.DomainModeling.Lowering;

// ═══════════════════════════════════════════════════════════════
// Transport surface — protocol conventions applied to domain facts
//
// This is a *convention-specific view* — it consumes the shared
// derived facts (AggregateModel, BehaviorModel, EffectTopology)
// and adds protocol-level decisions: resource hierarchy (parent
// context) and whether an entity is directly exposable.
//
// EffectTopology lives in TopologyModel.cs as a shared fact;
// TransportSurface may carry a reference for consumers that only
// receive the transport view.
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
public sealed class TransportEntity {
    public TransportEntity(string name, string? parentName, bool isExposable) {
        Name = name;
        ParentName = parentName;
        IsExposable = isExposable;
    }

    /// <summary>Entity name.</summary>
    public string Name { get; }

    /// <summary>
    /// The aggregate root that provides routing context for this entity.
    /// Null for root entities; set to the parent for children.
    /// </summary>
    public string? ParentName { get; }

    /// <summary>
    /// Default transport convention: roots are directly addressable.
    /// Non-roots nest under parents. Future protocols may override.
    /// </summary>
    public bool IsExposable { get; }
}