using Poly.Interpretation;

namespace Poly.Introspection.CommonLanguageRuntime;

public abstract class ClrTypeMember : ITypeMember {
    public abstract ClrTypeDefinition MemberType { get; }
    public abstract ClrTypeDefinition DeclaringType { get; }
    public abstract IEnumerable<ClrParameter>? Parameters { get; }
    public abstract string Name { get; }

    ITypeDefinition ITypeMember.MemberTypeDefinition => MemberType;
    ITypeDefinition ITypeMember.DeclaringTypeDefinition => DeclaringType;
    IEnumerable<IParameter>? ITypeMember.Parameters => Parameters;

    public abstract Value GetMemberAccessor(Value instance, params IEnumerable<Value>? parameters);

    public override string ToString() => $"{MemberType} {DeclaringType}.{Name}";
}