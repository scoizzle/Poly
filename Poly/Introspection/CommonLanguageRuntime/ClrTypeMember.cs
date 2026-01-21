namespace Poly.Introspection.CommonLanguageRuntime;

internal abstract class ClrTypeMember : ITypeMember {
    public abstract ClrTypeDefinition MemberTypeDefinition { get; }
    public abstract ClrTypeDefinition DeclaringTypeDefinition { get; }
    public abstract IEnumerable<ClrParameter>? Parameters { get; }
    public abstract string Name { get; }
    public abstract bool IsStatic { get; }

    ITypeDefinition ITypeMember.MemberTypeDefinition => MemberTypeDefinition;
    ITypeDefinition ITypeMember.DeclaringTypeDefinition => DeclaringTypeDefinition;
    IEnumerable<IParameter>? ITypeMember.Parameters => Parameters;

    public override string ToString() => $"{MemberTypeDefinition} {DeclaringTypeDefinition}.{Name}";
}