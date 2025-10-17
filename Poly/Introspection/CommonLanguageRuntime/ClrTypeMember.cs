using Poly.Interpretation;
using Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

namespace Poly.Introspection.CommonLanguageRuntime;
[DebuggerDisplay("{MemberType} {DeclaringType}.{Name}")]
public sealed class ClrTypeMember : ITypeMember {
    private readonly string _name;
    private readonly Lazy<ClrTypeDefinition> _memberType;
    private readonly ClrTypeDefinition _declaringType;
    public ClrTypeMember(Lazy<ClrTypeDefinition> memberType, ClrTypeDefinition declaringType, string name) {
        ArgumentNullException.ThrowIfNull(memberType);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _memberType = memberType;
        _declaringType = declaringType;
        _name = name;
    }

    public ClrTypeDefinition MemberType => _memberType.Value;
    public ClrTypeDefinition DeclaringType => _declaringType;
    public string Name => _name;

    ITypeDefinition ITypeMember.MemberType => MemberType;
    ITypeDefinition ITypeMember.DeclaringType => DeclaringType;

    public Value GetMemberAccessor(Value instance) => new ClrTypeMemberInterpretationAccessor(instance, this);

    public override string ToString() => $"{MemberType} {DeclaringType}.{Name}";
}