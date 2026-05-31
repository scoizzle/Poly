using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed record DomainTypeLookupMetadata(
    Domain Domain,
    IReadOnlyDictionary<string, DomainType> Types,
    IReadOnlySet<Entity> Entities) : IAnalysisMetadata;

public sealed record ResolvedTypeReferenceMetadata(DomainType Type) : IAnalysisMetadata;

public sealed record EffectivePoliciesMetadata(IReadOnlyList<Policy> Policies) : IAnalysisMetadata;