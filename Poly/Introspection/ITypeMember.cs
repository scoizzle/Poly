using Poly.Interpretation;

namespace Poly.Introspection;

public interface ITypeMember {
    ITypeDefinition MemberType { get; }
    ITypeDefinition DeclaringType { get; }
    string Name { get; }

    Value GetMemberAccessor(Value instance);
}