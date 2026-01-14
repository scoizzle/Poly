using System.Reflection;

using Poly.Interpretation;
using Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// Reflection-backed field member for a CLR type. Exposes the field's type,
/// declaring type, and name, and provides an accessor for interpretation.
/// Instances are immutable and safe for concurrent reads.
/// </summary>
[DebuggerDisplay("{MemberType} {DeclaringType}.{Name}")]
internal sealed class ClrTypeField : ClrTypeMember, ITypeField {
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
    public override IOrderedEnumerable<ClrParameter>? Parameters => null;

    /// <summary>
    /// Gets the field name.
    /// </summary>
    public override string Name => _fieldInfo.Name;

    /// <summary>
    /// Gets the underlying reflection <see cref="FieldInfo"/>.
    /// </summary>
    public FieldInfo FieldInfo => _fieldInfo;


    /// <summary>
    /// Gets whether this field is static.
    /// </summary>
    public override bool IsStatic => _fieldInfo.IsStatic;

    /// <summary>
    /// Creates an accessor that reads this field from the provided <paramref name="instance"/>.
    /// </summary>
    public override Value GetMemberAccessor(Value instance, params IEnumerable<Value>? parameters) => new ClrTypeFieldInterpretationAccessor(instance, this);

    public override string ToString() => $"{MemberTypeDefinition} {DeclaringTypeDefinition}.{Name}";
}