using System.Diagnostics.CodeAnalysis;

using Poly.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed record DomainTypeLookupMetadata(
    Domain Domain,
    IReadOnlyDictionary<string, DomainType> Types,
    IReadOnlySet<Entity> Entities) : IAnalysisMetadata;

public sealed record ResolvedTypeReferenceMetadata(DomainType Type) : IAnalysisMetadata;

public sealed record EffectivePoliciesMetadata(IReadOnlyList<Policy> Policies) : IAnalysisMetadata;

public sealed record EffectiveMemberMetadata(
    IReadOnlyList<Property> EffectiveProperties,
    IReadOnlyList<Action> EffectiveActions,
    IReadOnlyList<Policy> EffectivePolicies,
    IReadOnlyList<Stage> EffectiveStages
) : IAnalysisMetadata;

public sealed record RelationshipLookupMetadata(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, Relationship>> BySourceEntity
) : IAnalysisMetadata {
    /// <summary>
    /// Resolves a relationship by its owning (source) entity name and nav name.
    /// Relationship identity is (source entity, name) — two entities may each
    /// declare a navigation property with the same name.
    /// </summary>
    public bool TryGetRelationship(string sourceEntityName, string relationshipName, [NotNullWhen(true)] out Relationship? relationship) {
        if (BySourceEntity.TryGetValue(sourceEntityName, out var byNav)
            && byNav.TryGetValue(relationshipName, out var rel)) {
            relationship = rel;
            return true;
        }
        relationship = null;
        return false;
    }

    /// <summary>
    /// Relationships with the given name declared on any source entity. Used by
    /// diagnostics that must distinguish "exists but wrong direction/source"
    /// from "name unknown" after a source-scoped lookup misses.
    /// </summary>
    public IEnumerable<Relationship> FindByNameAcrossSources(string relationshipName) {
        foreach (var byNav in BySourceEntity.Values) {
            if (byNav.TryGetValue(relationshipName, out var rel))
                yield return rel;
        }
    }
}

public sealed record ResolvedRelationshipTargetMetadata(
    Relationship Relationship,
    Entity TargetEntity
) : IAnalysisMetadata;

public sealed record ActionResolutionMetadata(
    IReadOnlyDictionary<string, Action> EntityActions,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, Action>> StageActions
) : IAnalysisMetadata;

public sealed record RelationshipContractMetadata(
    IReadOnlyList<RelationshipContract> Contracts
) : IAnalysisMetadata;

public sealed record RelationshipContract(
    string Name,
    string SourceEntityName,
    string TargetEntityName,
    RelationshipCardinality Cardinality,
    bool SourceOwnsTarget
);

/// <summary>
/// Subscription dispatch plan keyed by relationship name.
/// Published on <see cref="Stage"/> for stage-scoped <c>when</c>, and on
/// <see cref="Entity"/> for always-active entity-level <see cref="Entity.Subscriptions"/>
/// (same entry shape; entity-level bags entity plans on the entity node).
/// </summary>
public sealed record SubscriptionDispatchPlanMetadata(
    IReadOnlyDictionary<string, IReadOnlyList<SubscriptionDispatchPlanEntry>> ByRelationshipName
) : IAnalysisMetadata;

public sealed record SubscriptionDispatchPlanEntry(
    string RelationshipName,
    string SourceEntityName,
    string TargetEntityName,
    StageSubscriptionQuantifier Quantifier,
    IReadOnlySet<string> StageNames,
    IReadOnlyList<Effect> Effects,
    string? PeerBinding = null
);

public sealed record MutationTargetIndexMetadata(
    IReadOnlyDictionary<string, DomainType> TypesByName,
    IReadOnlyDictionary<string, Entity> EntitiesByName,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, Relationship>> RelationshipsByName,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, Stage>> StagesByEntity,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Action>>> ActionsByEntity,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, Policy>> EntityPoliciesByEntity,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, Policy>>> StagePoliciesByEntity,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, Policy>>> ActionPoliciesByEntity
) : IAnalysisMetadata;