namespace Poly.DomainModeling.Effects;

/// <summary>
/// Effect that invokes an action, either on the current instance (E3a, self-only)
/// or on a related entity instance reachable through a relationship (E3b).
/// </summary>
/// <param name="ActionName">The action to invoke.</param>
/// <param name="ParameterBindings">Optional argument bindings.</param>
/// <param name="TargetRelationship">
/// When <c>null</c>, invoke is self-only (E3a).
/// When non-null, outbound navigate from the relationship <b>source</b> only
/// (fail-closed; reverse-side / ManyToMany / self-rel rejected until analyzable).
/// </param>
/// <param name="Quantifier">
/// <c>null</c> → singular OneToOne outbound invoke.
/// <c>Any</c> / <c>All</c> → OneToMany outbound; empty match set fails (no vacuous success).
/// <c>Each</c> is invalid on invoke.
/// </param>
/// <param name="Filter">
/// Optional target-local predicate. Allowed only with <c>Any</c>/<c>All</c> on OneToMany.
/// Restricted expression surface (local props/literals/comparisons/bool/arithmetic only).
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