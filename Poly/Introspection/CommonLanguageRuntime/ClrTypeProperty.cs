using System.Reflection;
using System.Runtime.CompilerServices;

using Poly.Introspection;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// Reflection-backed property member for a CLR type. Supports both regular properties and
/// indexer properties (with parameters). Instances are immutable and safe for concurrent reads.
/// </summary>
[DebuggerDisplay("{MemberType} {DeclaringType}.{Name}")]
internal sealed class ClrTypeProperty : ClrPropertyMember {
    private readonly Lazy<ClrTypeDefinition> _memberType;
    private readonly ClrTypeDefinition _declaringType;
    private readonly PropertyInfo _propertyInfo;
    private readonly IEnumerable<ClrParameter>? _parameters;
    private readonly string _name;
    private readonly LifetimeModifier _lifetimeModifier;
    private readonly AccessModifier _accessModifier;

    private readonly MemberReadDelegate? _read;
    private readonly MemberWriteDelegate? _write;
    private readonly MemberWriteDelegate? _initialize;
    private readonly bool _isReadOnly;

    public ClrTypeProperty(Lazy<ClrTypeDefinition> memberType, ClrTypeDefinition declaringType, IEnumerable<ClrParameter>? parameters, PropertyInfo propertyInfo) {
        ArgumentNullException.ThrowIfNull(memberType);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(propertyInfo);

        _memberType = memberType;
        _declaringType = declaringType;
        _parameters = parameters;
        _propertyInfo = propertyInfo;
        _name = propertyInfo.Name;
        _lifetimeModifier = propertyInfo.GetGetMethod(nonPublic: true)?.IsStatic == true ||
                            propertyInfo.GetSetMethod(nonPublic: true)?.IsStatic == true
            ? LifetimeModifier.Static
            : LifetimeModifier.Instance;
        _accessModifier = ClrAccessModifierResolver.Resolve(propertyInfo);

        var getter = propertyInfo.GetGetMethod(nonPublic: true);
        if (getter is not null) {
            _read = (owner, arguments) => {
                var args = arguments is { Length: > 0 } ? arguments : null;
                var target = getter.IsStatic ? null : owner;
                return propertyInfo.GetValue(target, args);
            };
        }

        var setter = propertyInfo.GetSetMethod(nonPublic: true);
        if (setter is not null) {
            if (IsInitOnlySetter(setter)) {
                _initialize = (owner, value, arguments) => {
                    var args = arguments is { Length: > 0 } ? arguments : null;
                    var target = setter.IsStatic ? null : owner;
                    propertyInfo.SetValue(target, value, args);
                    return owner;
                };
                _write = null;
            }
            else {
                _write = (owner, value, arguments) => {
                    var args = arguments is { Length: > 0 } ? arguments : null;
                    var target = setter.IsStatic ? null : owner;
                    propertyInfo.SetValue(target, value, args);
                    return owner;
                };
                _initialize = null;
            }
        }

        _isReadOnly = setter is null || (setter != null && IsInitOnlySetter(setter));
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

    public override MemberReadDelegate? Read => _read;
    public override MemberWriteDelegate? Write => _write;
    public override MemberWriteDelegate? Initialize => _initialize;

    /// <summary>
    /// Gets the property visibility.
    /// </summary>
    public override AccessModifier AccessModifier => _accessModifier;

    /// <summary>
    /// Gets whether this property's getter or setter is static.
    /// </summary>
    public override LifetimeModifier LifetimeModifier => _lifetimeModifier;

    public override Mutability Mutability {
        get {
            var m = Mutability.Mutable;
            if (_isReadOnly) m |= Mutability.ReadOnlyAfterInit;
            // IsConst remains false (safe fallback for properties)
            // VolatileAccess: not easily detectable for properties; default Mutable
            return m;
        }
    }

    public override string ToString() => $"{MemberTypeDefinition} {DeclaringTypeDefinition}.{Name}{(_parameters is null ? string.Empty : $"[{string.Join(", ", _parameters)}]")}";

    private static bool IsInitOnlySetter(MethodInfo setter) {
        var requiredModifiers = setter.ReturnParameter.GetRequiredCustomModifiers();
        return requiredModifiers.Contains(typeof(IsExternalInit));
    }
}