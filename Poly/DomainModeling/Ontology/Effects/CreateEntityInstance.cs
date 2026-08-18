using Poly.DomainModeling.Runtime;

namespace Poly.DomainModeling.Ontology.Effects;

/// <summary>
/// Effect that creates a new instance of an entity (or owned structure).
/// When <paramref name="RelationshipName"/> is set, the created instance is
/// automatically linked to the creator via <see cref="DomainInstanceStore.Link"/>,
/// with the creator as the relationship source and the child as the target.
/// This enables subscription fan-out without manual <c>store.Link</c> calls.
/// </summary>
public sealed record CreateEntityInstance(
    DomainTypeReference Type,
    IReadOnlyList<PropertyBinding> Initializers,
    string? RelationshipName = null
) : Effect(GetInvocationResult(Type)) {
    public CreateEntityInstance(DomainTypeReference type)
        : this(type, [], null) { }

    private static InvocationResult GetInvocationResult(DomainTypeReference type) =>
        new([new InvocationResult.Member("Instance", new DomainTypeReference(type.TypeName), [])]);

    public sealed override IEnumerable<Node?> Children => [Result, Type, .. Initializers];
}