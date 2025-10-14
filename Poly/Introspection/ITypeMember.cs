namespace Poly.Introspection;

public interface ITypeMember {
    ITypeDefinition MemberType { get; }
    ITypeDefinition DeclaringType { get; }
    string Name { get; }
}