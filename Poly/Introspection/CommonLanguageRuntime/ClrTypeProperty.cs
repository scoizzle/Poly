using System.Reflection;

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

    private readonly bool _isReadOnly;
    private readonly bool _hasInitSetter;

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

        var setMethod = propertyInfo.GetSetMethod(nonPublic: true);
        _isReadOnly = setMethod is null;
        _hasInitSetter = setMethod?.ReturnParameter
            .GetRequiredCustomModifiers()
            .Contains(typeof(IsExternalInit)) == true;
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
    public override IEnumerable<ClrParameter> Parameters => _parameters ?? [];

    /// <summary>
    /// Gets the property name.
    /// </summary>
    public override string Name => _name;

    /// <summary>
    /// Gets the underlying reflection <see cref="PropertyInfo"/>.
    /// </summary>
    public PropertyInfo PropertyInfo => _propertyInfo;


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
            if (_hasInitSetter) m |= Mutability.ReadOnlyAfterInit;
            return m;
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the property has a readable getter.
    /// </summary>
    public new bool CanRead => base.CanRead;

    /// <summary>
    /// Returns <c>true</c> when the property has a writable setter (not init-only).
    /// </summary>
    public new bool CanWrite => !_isReadOnly && !_hasInitSetter;

    /// <summary>
    /// Returns <c>true</c> when the property has an init-only setter.
    /// </summary>
    public new bool CanInitialize => _hasInitSetter;

    public override string ToString() => $"{MemberTypeDefinition} {DeclaringTypeDefinition}.{Name}{(_parameters is null ? string.Empty : $"[{string.Join(", ", _parameters)}]")}";

    public override Expression? EmitRead(Expression? instance) {
        if (IsStatic || instance is not null) {
            var typedInst = IsStatic ? null : System.Linq.Expressions.Expression.Convert(instance!, _propertyInfo.DeclaringType!);
            var access = System.Linq.Expressions.Expression.Property(typedInst, _propertyInfo);
            return _propertyInfo.PropertyType.IsValueType
                ? System.Linq.Expressions.Expression.Convert(access, typeof(object))
                : access;
        }
        return null;
    }

    public override Expression? EmitWrite(Expression? instance, Expression value) {
        if (_isReadOnly) return null;
        var typedInst = IsStatic ? null : System.Linq.Expressions.Expression.Convert(instance!, _propertyInfo.DeclaringType!);
        var val = System.Linq.Expressions.Expression.Convert(value, _propertyInfo.PropertyType);
        var assign = System.Linq.Expressions.Expression.Assign(
            System.Linq.Expressions.Expression.Property(typedInst, _propertyInfo), val);
        return IsStatic ? assign : System.Linq.Expressions.Expression.Block(assign, instance!);
    }
}