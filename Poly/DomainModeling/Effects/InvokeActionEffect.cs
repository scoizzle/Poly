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
/// Singular (OneToOne) only — OneToMany fan-out uses <see cref="ForEachInvokeEffect"/>.
/// </param>
public sealed record InvokeActionEffect(
    string ActionName,
    IReadOnlyList<PropertyBinding> ParameterBindings,
    string? TargetRelationship = null
) : Effect {
    public override IEnumerable<Node?> Children => [.. ParameterBindings];
}