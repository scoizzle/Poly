using System.Reflection;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// Reflection-backed field member for a CLR type. Exposes the field's type,
/// declaring type, and name, and provides delegates for direct interpretation access.
/// Instances are immutable and safe for concurrent reads.
/// </summary>
[DebuggerDisplay("{MemberType} {DeclaringType}.{Name}")]
internal sealed class ClrTypeField : ClrTypeMember, ITypeField {
    private readonly Lazy<ClrTypeDefinition> _memberType;
    private readonly ClrTypeDefinition _declaringType;
    private readonly bool _isVolatile;

    public ClrTypeField(Lazy<ClrTypeDefinition> memberType, ClrTypeDefinition declaringType, FieldInfo fieldInfo) {
        ArgumentNullException.ThrowIfNull(memberType);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(fieldInfo);

        _memberType = memberType;
        _declaringType = declaringType;
        FieldInfo = fieldInfo;

        _isVolatile = fieldInfo.GetRequiredCustomModifiers().Any(t => t == typeof(IsVolatile));
    }

    /// <summary>
    /// Gets the field type definition.
    /// </summary>
    public override ClrTypeDefinition MemberTypeDefinition => _memberType.Value;

    /// <summary>
    /// Gets the declaring type definition that owns this field.
    /// </summary>
    public override ClrTypeDefinition DeclaringTypeDefinition => _declaringType;

    /// <summary>
    /// Fields do not have parameters; always null.
    /// </summary>
    public override IEnumerable<ClrParameter> Parameters => [];

    /// <summary>
    /// Gets the field name.
    /// </summary>
    public override string Name => FieldInfo.Name;

    public override MemberKind Kind => MemberKind.Field;

    /// <summary>
    /// Gets the underlying reflection <see cref="FieldInfo"/>.
    /// </summary>
    public FieldInfo FieldInfo { get; }

    /* Read/Write delegates removed — emitter uses FieldInfo directly */

    public override Mutability Mutability {
        get {
            var m = Mutability.Mutable;
            if (FieldInfo.IsInitOnly || FieldInfo.IsLiteral) m |= Mutability.ReadOnlyAfterInit;
            if (FieldInfo.IsLiteral) m |= Mutability.CompileTimeConst;
            if (_isVolatile) m |= Mutability.VolatileAccess;
            return m;
        }
    }

    /// <summary>
    /// Gets the field visibility.
    /// </summary>
    public override AccessModifier AccessModifier => ClrAccessModifierResolver.Resolve(FieldInfo);

    /// <summary>
    /// Gets whether this field is static or instance-scoped.
    /// </summary>
    public override LifetimeModifier LifetimeModifier => FieldInfo.IsStatic ? LifetimeModifier.Static : LifetimeModifier.Instance;

    public override string ToString() => $"{MemberTypeDefinition} {DeclaringTypeDefinition}.{Name}";
}