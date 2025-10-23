using System.Reflection;

using Poly.Interpretation;
using Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

namespace Poly.Introspection.CommonLanguageRuntime;

[DebuggerDisplay("{MemberType} {DeclaringType}.{Name}")]
public sealed class ClrTypeProperty : ClrTypeMember {
    private readonly Lazy<ClrTypeDefinition> _memberType;
    private readonly ClrTypeDefinition _declaringType;
    private readonly PropertyInfo _propertyInfo;
    private readonly string _name;

    public ClrTypeProperty(Lazy<ClrTypeDefinition> memberType, ClrTypeDefinition declaringType, PropertyInfo propertyInfo) {
        ArgumentNullException.ThrowIfNull(memberType);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(propertyInfo);

        _memberType = memberType;
        _declaringType = declaringType;
        _propertyInfo = propertyInfo;
        _name = propertyInfo.Name;
    }

    public override ClrTypeDefinition MemberType => _memberType.Value;
    public override ClrTypeDefinition DeclaringType => _declaringType;
    public override string Name => _name;
    public PropertyInfo PropertyInfo => _propertyInfo;

    public override Value GetMemberAccessor(Value instance) => new ClrTypePropertyInterpretationAccessor(instance, this);

    public override string ToString() => $"{MemberType} {DeclaringType}.{Name}";
}