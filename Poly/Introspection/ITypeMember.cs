using Poly.Interpretation;

namespace Poly.Introspection;

public interface ITypeMember {
    ITypeDefinition MemberTypeDefinition { get; }
    ITypeDefinition DeclaringTypeDefinition { get; }
    string Name { get; }
    IEnumerable<IParameter>? Parameters { get; }

    Value GetMemberAccessor(Value instance, params IEnumerable<Value>? parameters);
}