namespace Poly.DataModeling.Interpretation;

/// <summary>
/// Accesses a dynamic object's property by name using IDictionary<string, object?> semantics.
/// </summary>
internal sealed record DataModelPropertyAccessor(Node Instance, string PropertyName, ITypeDefinition MemberType) : Node {
    public override string ToString() => $"{Instance}.{PropertyName}";
}