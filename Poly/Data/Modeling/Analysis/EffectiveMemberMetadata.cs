namespace Poly.Data.Modeling;

/// <summary>
/// Analyzer metadata for effective members (properties, actions, policies, events, relationships, stages) of an entity.
/// </summary>
public sealed record EffectiveMemberMetadata : IAnalysisMetadata {
    public IReadOnlyCollection<Property> EffectiveProperties { get; init; } = new List<Property>();
    public IReadOnlyCollection<Action> EffectiveActions { get; init; } = new List<Action>();
    public IReadOnlyCollection<Policy> EffectivePolicies { get; init; } = new List<Policy>();
    public IReadOnlyCollection<Event> EffectiveEvents { get; init; } = new List<Event>();
    public IReadOnlyCollection<Relationship> EffectiveRelationships { get; init; } = new List<Relationship>();
    public IReadOnlyCollection<Stage> EffectiveStages { get; init; } = new List<Stage>();
}