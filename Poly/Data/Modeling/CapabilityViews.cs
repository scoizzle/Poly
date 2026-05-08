using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed record ActionCapabilityView(
    string ActionName,
    IReadOnlyCollection<Property> Parameters,
    IReadOnlyCollection<Effect> Effects,
    IReadOnlyCollection<Type> EffectTypes,
    IReadOnlyCollection<Event> PublishedEvents,
    IReadOnlyCollection<Stage> TransitionTargets,
    ActionTrigger Trigger);

public sealed record EventSubscriptionCapabilityView(
    string EventTypeName,
    string HandlerActionName,
    EventSubscriptionAudience Audience,
    IReadOnlyCollection<EventCorrelationBinding> Correlations);

public sealed record StageCapabilityView(
    string StageName,
    IReadOnlyCollection<ActionCapabilityView> LocalActions,
    IReadOnlyCollection<ActionCapabilityView> EffectiveActions,
    IReadOnlyCollection<Policy> LocalPolicies,
    IReadOnlyCollection<Policy> EffectivePolicies);

public sealed record RelationshipCapabilityView(
    string RelationshipName,
    DomainType Source,
    DomainType Target,
    RelationshipCardinality Cardinality,
    bool SourceOwnsTarget,
    IReadOnlyCollection<Property> Properties,
    IReadOnlyCollection<Stage> Stages,
    IReadOnlyCollection<Policy> Policies);