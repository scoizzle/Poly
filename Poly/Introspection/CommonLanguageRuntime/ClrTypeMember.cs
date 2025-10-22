using Poly.Interpretation;

namespace Poly.Introspection.CommonLanguageRuntime;

public abstract class ClrTypeMember : ITypeMember {
    public abstract ClrTypeDefinition MemberType { get; }
    public abstract ClrTypeDefinition DeclaringType { get; }
    public abstract string Name { get; }

    ITypeDefinition ITypeMember.MemberTypeDefinition => MemberType;
    ITypeDefinition ITypeMember.DeclaringTypeDefinition => DeclaringType;

    public abstract Value GetMemberAccessor(Value instance);

    public override string ToString() => $"{MemberType} {DeclaringType}.{Name}";
}
