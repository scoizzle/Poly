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
    IReadOnlyDictionary<string, Relationship> Relationships
) : IAnalysisMetadata;

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

public sealed record SubscriptionDispatchPlanMetadata(
    IReadOnlyDictionary<string, IReadOnlyList<SubscriptionDispatchPlanEntry>> ByRelationshipName
) : IAnalysisMetadata;

public sealed record SubscriptionDispatchPlanEntry(
    string RelationshipName,
    string SourceEntityName,
    string TargetEntityName,
    StageSubscriptionQuantifier Quantifier,
    IReadOnlySet<string> StageNames,
    IReadOnlyList<Effect> Effects
);

public sealed record MutationTargetIndexMetadata(
    IReadOnlyDictionary<string, DomainType> TypesByName,
    IReadOnlyDictionary<string, Entity> EntitiesByName,
    IReadOnlyDictionary<string, Relationship> RelationshipsByName,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, Stage>> StagesByEntity,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Action>>> ActionsByEntity,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, Policy>> EntityPoliciesByEntity,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, Policy>>> StagePoliciesByEntity,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, Policy>>> ActionPoliciesByEntity
) : IAnalysisMetadata;