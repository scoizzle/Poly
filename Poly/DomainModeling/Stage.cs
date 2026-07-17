namespace Poly.DomainModeling;

public sealed record StageReference(string StageName) : DomainObject;

/// <summary>
/// Stages define the valid states an <see cref="Entity"/> can be in, along with the actions available in that state,
/// policies that must hold, and effects that occur upon entering or leaving the stage.
/// </summary>
/// <remarks>
/// Stages form a hierarchy via <see cref="Parent"/>. Effective actions and policies are computed by analyzers
/// by walking the parent chain. <see cref="OnEntryEffects"/> and <see cref="OnExitEffects"/> allow modeling
/// automatic behavior triggered by state transitions.
/// </remarks>
public sealed record Stage(
    string Name,
    StageReference? Parent,
    IReadOnlyList<Action> Actions,
    IReadOnlyList<Policy> Policies,
    IReadOnlyList<Effect> OnEntryEffects,
    IReadOnlyList<Effect> OnExitEffects
) : DomainMember(Name) {
    /// <summary>
    /// Stage-scoped subscriptions that fire when a related entity transitions into a matching stage.
    /// Active only while an entity occupies this stage.
    /// </summary>
    public IReadOnlyList<StageSubscription> Subscriptions { get; init; } = [];

    public sealed override IEnumerable<Node?> Children =>
        [Parent, .. Actions, .. Policies, .. OnEntryEffects, .. OnExitEffects, .. Subscriptions];
}