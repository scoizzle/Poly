using System.Reflection;

using Poly.Interpretation;
using Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// Reflection-backed property member for a CLR type. Supports both regular properties and
/// indexer properties (with parameters). Instances are immutable and safe for concurrent reads.
/// </summary>
[DebuggerDisplay("{MemberType} {DeclaringType}.{Name}")]
internal sealed class ClrTypeProperty : ClrTypeMember, ITypeProperty {
    private readonly Lazy<ClrTypeDefinition> _memberType;
    private readonly ClrTypeDefinition _declaringType;
    private readonly PropertyInfo _propertyInfo;
    private readonly IEnumerable<ClrParameter>? _parameters;
    private readonly string _name;
    private readonly bool _isStatic;

    public ClrTypeProperty(Lazy<ClrTypeDefinition> memberType, ClrTypeDefinition declaringType, IEnumerable<ClrParameter>? parameters, PropertyInfo propertyInfo)
    {
        ArgumentNullException.ThrowIfNull(memberType);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(propertyInfo);

        _memberType = memberType;
        _declaringType = declaringType;
        _parameters = parameters;
        _propertyInfo = propertyInfo;
        _name = propertyInfo.Name;
        _isStatic = (propertyInfo.GetGetMethod(nonPublic: true)?.IsStatic ?? false) ||
                    (propertyInfo.GetSetMethod(nonPublic: true)?.IsStatic ?? false);
    }

    /// <summary>
    /// Gets the property type definition.
    /// </summary>
    public override ClrTypeDefinition MemberTypeDefinition => _memberType.Value;

    /// <summary>
    /// Gets the declaring type definition that owns this property.
    /// </summary>
    public override ClrTypeDefinition DeclaringTypeDefinition => _declaringType;

    /// <summary>
    /// Gets the index parameters for an indexer property, or null for regular properties.
    /// </summary>
    public override IEnumerable<ClrParameter>? Parameters => _parameters;

    /// <summary>
    /// Gets the property name.
    /// </summary>
    public override string Name => _name;

    /// <summary>
    /// Gets the underlying reflection <see cref="PropertyInfo"/>.
    /// </summary>
    public PropertyInfo PropertyInfo => _propertyInfo;

    /// <summary>
    /// Gets whether this property's getter or setter is static.
    /// </summary>
    public override bool IsStatic => _isStatic;

    /// <summary>
    /// Creates an accessor that reads this property (or indexer) from <paramref name="instance"/>.
    /// Validates parameter counts for indexers.
    /// </summary>
    public override Value GetMemberAccessor(Value instance, params IEnumerable<Value>? parameters)
    {
        if (_parameters is not null) {
            if (parameters is null || parameters.Count() != _parameters.Count()) {
                throw new ArgumentException($"Indexer property '{Name}' requires {_parameters.Count()} parameters, but {parameters?.Count() ?? 0} were provided.");
            }

            return new ClrTypeIndexInterpretationAccessor(instance, this, parameters);
        }
        else {
            return new ClrTypePropertyInterpretationAccessor(instance, this);
        }
    }

    public override string ToString() => $"{MemberTypeDefinition} {DeclaringTypeDefinition}.{Name}{(_parameters is null ? string.Empty : $"[{string.Join(", ", _parameters)}]")}";
}