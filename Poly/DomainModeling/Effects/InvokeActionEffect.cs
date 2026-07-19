namespace Poly.DomainModeling.Effects;

/// <summary>
/// Effect that invokes an action, either on the current instance (E3a, self-only)
/// or on a related entity instance reachable through a relationship (E3b).
/// </summary>
/// <param name="ActionName">The action to invoke.</param>
/// <param name="ParameterBindings">Optional argument bindings.</param>
/// <param name="TargetRelationship">
/// When <c>null</c>, invoke is self-only (E3a, current behavior).
/// When non-null, the target instance is resolved at runtime from the store
/// via the named relationship.
/// </param>
/// <param name="Quantifier">
/// <c>null</c> or <c>Each</c> → singular (exactly one target).
/// <c>Any</c> → try each linked target, return first success.
/// <c>All</c> → invoke on every linked target, fail on first miss.
/// Only meaningful with <see cref="TargetRelationship"/> on a <c>many</c> relationship.
/// </param>
/// <param name="Filter">
/// Optional predicate expression evaluated against each potential target
/// instance before invoking. Only targets passing the filter are invoked.
/// </param>
public sealed record InvokeActionEffect(
    string ActionName,
    IReadOnlyList<PropertyBinding> ParameterBindings,
    string? TargetRelationship = null,
    StageSubscriptionQuantifier? Quantifier = null,
    DomainExpression? Filter = null
) : Effect {
    public sealed override IEnumerable<Node?> Children => [.. ParameterBindings, Filter];
}