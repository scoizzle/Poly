namespace Poly.DomainModeling.Ontology.Effects;

/// <summary>
/// Effect that creates a new instance of an entity (or owned structure).
/// When <paramref name="RelationshipName"/> is set, the lowered module links
/// the created instance to the creator, with the creator as the relationship
/// source and the child as the target.
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