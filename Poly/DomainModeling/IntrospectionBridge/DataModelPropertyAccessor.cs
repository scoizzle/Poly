namespace Poly.DomainModeling.IntrospectionBridge;

/// <summary>
/// Accesses a dynamic object's property by name using IDictionary<string, object?> semantics.
/// </summary>
public sealed record DataModelPropertyAccessor(Node Instance, string PropertyName, ITypeDefinition MemberType) : Node {
    public override IEnumerable<Node?> Children => [Instance];

    public override string ToString() => $"{Instance}.{PropertyName}";
}