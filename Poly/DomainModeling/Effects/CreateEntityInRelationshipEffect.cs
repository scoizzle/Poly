namespace Poly.DomainModeling.Effects;

/// <summary>
/// Effect that creates an entity instance using a relationship name to determine
/// the target entity type. The relationship is resolved at runtime from the
/// domain model, and the created instance is automatically linked to the creator.
/// </summary>
public sealed record CreateEntityInRelationshipEffect(
    string RelationshipName,
    IReadOnlyList<PropertyBinding> Initializers
) : Effect(GetInvocationResult()) {
    private static InvocationResult GetInvocationResult() =>
        new(InvocationResult.Void.Members);

    public sealed override IEnumerable<Node?> Children => [Result, .. Initializers];
}