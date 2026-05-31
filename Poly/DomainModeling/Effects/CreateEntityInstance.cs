namespace Poly.DomainModeling.Effects;

/// <summary>
/// Effect that creates a new instance of an entity (or owned structure).
/// 
/// Supports initialization of properties using <see cref="DomainExpression"/> bindings.
/// </summary>
public sealed record CreateEntityInstance(
    DomainTypeReference Type,
    IReadOnlyList<PropertyBinding> Initializers
) : Effect(GetInvocationResult(Type)) {
    public CreateEntityInstance(DomainTypeReference type)
        : this(type, []) { }

    private static InvocationResult GetInvocationResult(DomainTypeReference type) =>
        new([new InvocationResult.Member("Instance", new DomainTypeReference(type.TypeName), [])]);

    public sealed override IEnumerable<Node?> Children => [Result, Type, .. Initializers];
}