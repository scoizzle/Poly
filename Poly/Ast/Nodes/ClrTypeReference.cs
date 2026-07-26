namespace Poly.Ast.Nodes;

public sealed record ClrTypeReference(Type RuntimeType) : TypeReference(RuntimeType.FullName ?? RuntimeType.Name) {
    public override string ToString() => TypeName;
}