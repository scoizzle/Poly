namespace Poly.DomainModeling.Effects;

/// <summary>
/// Effect that creates an entity instance using a relationship name to determine
/// the target entity type. The relationship is resolved at runtime from the
/// domain model, and the created instance is automatically linked to the creator.
///
/// When <paramref name="ResolvedTargetType"/> is provided (e.g. after analysis
/// resolves the relationship), the effect declares an "Instance" result member
/// matching the target type — otherwise the invocation result is void.
/// </summary>
public sealed record CreateEntityInRelationshipEffect(
    string RelationshipName,
    IReadOnlyList<PropertyBinding> Initializers,
    DomainTypeReference? ResolvedTargetType = null
) : Effect(ResolvedTargetType is not null
    ? new InvocationResult([new InvocationResult.Member("Instance", ResolvedTargetType, [])])
    : InvocationResult.Void) {
    public sealed override IEnumerable<Node?> Children => [Result, .. Initializers];
}