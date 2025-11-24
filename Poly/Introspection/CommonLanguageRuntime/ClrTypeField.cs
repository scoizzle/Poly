using System.Reflection;

using Poly.Interpretation;
using Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

namespace Poly.Introspection.CommonLanguageRuntime;

[DebuggerDisplay("{MemberType} {DeclaringType}.{Name}")]
public sealed class ClrTypeField : ClrTypeMember {
    private readonly Lazy<ClrTypeDefinition> _memberType;
    private readonly ClrTypeDefinition _declaringType;
    private readonly FieldInfo _fieldInfo;

    public ClrTypeField(Lazy<ClrTypeDefinition> memberType, ClrTypeDefinition declaringType, FieldInfo fieldInfo) {
        ArgumentNullException.ThrowIfNull(memberType);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(fieldInfo);

        _memberType = memberType;
        _declaringType = declaringType;
        _fieldInfo = fieldInfo;
    }

    public override ClrTypeDefinition MemberType => _memberType.Value;
    public override ClrTypeDefinition DeclaringType => _declaringType;
    public override IOrderedEnumerable<ClrParameter>? Parameters => null;
    public override string Name => _fieldInfo.Name;
    public FieldInfo FieldInfo => _fieldInfo;


    public override Value GetMemberAccessor(Value instance, params IEnumerable<Value>? parameters) => new ClrTypeFieldInterpretationAccessor(instance, this);

    public override string ToString() => $"{MemberType} {DeclaringType}.{Name}";
}