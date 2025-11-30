using Poly.Interpretation;

namespace Poly.Introspection;

public interface ITypeMember {
    string Name { get; }
    ITypeDefinition MemberTypeDefinition { get; }
    ITypeDefinition DeclaringTypeDefinition { get; }
    IEnumerable<IParameter>? Parameters { get; }

    Value GetMemberAccessor(Value instance, params IEnumerable<Value>? parameters);
}