using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed record DomainTypeLookupMetadata(
    Domain Domain,
    IReadOnlyDictionary<string, DomainType> Types,
    IReadOnlySet<Entity> Entities) : IAnalysisMetadata;

public sealed record ResolvedTypeReferenceMetadata(DomainType Type) : IAnalysisMetadata;

public sealed record EffectivePoliciesMetadata(IReadOnlyList<Policy> Policies) : IAnalysisMetadata;

internal sealed record ResolvedStageParentMetadata(Stage ParentStage) : IAnalysisMetadata;

public sealed record EffectiveMemberMetadata(
    IReadOnlyList<Property> EffectiveProperties,
    IReadOnlyList<Action> EffectiveActions,
    IReadOnlyList<Policy> EffectivePolicies,
    IReadOnlyList<DomainTypeReference> EffectiveEvents,
    IReadOnlyList<Stage> EffectiveStages
) : IAnalysisMetadata;

public sealed record StageLineageMetadata(
    int Depth,
    IReadOnlyList<Stage> Ancestors
) : IAnalysisMetadata;