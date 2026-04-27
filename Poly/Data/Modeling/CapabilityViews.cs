using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed record ActionCapabilityView(
    string ActionName,
    IReadOnlyCollection<Property> Parameters,
    IReadOnlyCollection<Effect> Effects,
    IReadOnlyCollection<Type> EffectTypes,
    IReadOnlyCollection<Event> PublishedEvents,
    IReadOnlyCollection<Stage> TransitionTargets);

public sealed record StageCapabilityView(
    string StageName,
    IReadOnlyCollection<ActionCapabilityView> LocalActions,
    IReadOnlyCollection<ActionCapabilityView> EffectiveActions,
    IReadOnlyCollection<Policy> LocalPolicies,
    IReadOnlyCollection<Policy> EffectivePolicies);

public sealed record RelationshipCapabilityView(
    string RelationshipName,
    IDomainType Source,
    IDomainType Target,
    RelationshipCardinality Cardinality,
    bool SourceOwnsTarget,
    IReadOnlyCollection<Property> Properties,
    IReadOnlyCollection<Stage> Stages,
    IReadOnlyCollection<Policy> Policies);