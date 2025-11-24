using System.Reflection;

using Poly.Interpretation;
using Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

namespace Poly.Introspection.CommonLanguageRuntime;

[DebuggerDisplay("{MemberType} {DeclaringType}.{Name}")]
public sealed class ClrTypeProperty : ClrTypeMember {
    private readonly Lazy<ClrTypeDefinition> _memberType;
    private readonly ClrTypeDefinition _declaringType;
    private readonly PropertyInfo _propertyInfo;
    private readonly IEnumerable<ClrParameter>? _parameters;
    private readonly string _name;

    public ClrTypeProperty(Lazy<ClrTypeDefinition> memberType, ClrTypeDefinition declaringType, IEnumerable<ClrParameter>? parameters, PropertyInfo propertyInfo) {
        ArgumentNullException.ThrowIfNull(memberType);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(propertyInfo);

        _memberType = memberType;
        _declaringType = declaringType;
        _parameters = parameters;
        _propertyInfo = propertyInfo;
        _name = propertyInfo.Name;
    }

    public override ClrTypeDefinition MemberType => _memberType.Value;
    public override ClrTypeDefinition DeclaringType => _declaringType;
    public override IEnumerable<ClrParameter>? Parameters => _parameters;
    public override string Name => _name;
    public PropertyInfo PropertyInfo => _propertyInfo;

    public override Value GetMemberAccessor(Value instance, params IEnumerable<Value>? parameters) {
        if (_parameters is not null) {
            if (parameters is null || parameters.Count() != _parameters.Count()) {
                throw new ArgumentException($"Indexer property '{Name}' requires {_parameters.Count()} parameters, but {parameters?.Count() ?? 0} were provided.");
            }

            return new ClrTypeIndexInterpretationAccessor(instance, this, parameters);
        } else {
            return new ClrTypePropertyInterpretationAccessor(instance, this);
        }
    }

    public override string ToString() => $"{MemberType} {DeclaringType}.{Name}{(_parameters is null ? string.Empty : $"[{string.Join(", ", _parameters)}]")}";
}