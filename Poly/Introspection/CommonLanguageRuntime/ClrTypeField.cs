using System.Linq.Expressions;
using System.Reflection;

using Poly.Introspection;

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

        _isVolatile = fieldInfo.GetRequiredCustomModifiers().Any(t => t == typeof(System.Runtime.CompilerServices.IsVolatile));

        Read = BuildFieldGetter(fieldInfo);

        if (fieldInfo.IsInitOnly || fieldInfo.IsLiteral) {
            Initialize = BuildFieldSetter(fieldInfo);
            Write = null;
        }
        else {
            Write = BuildFieldSetter(fieldInfo);
            Initialize = null;
        }
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
    public override string Name => FieldInfo.Name;

    /// <summary>
    /// Gets the underlying reflection <see cref="FieldInfo"/>.
    /// </summary>
    public FieldInfo FieldInfo { get; }

    public MemberReadDelegate? Read { get; }
    public MemberWriteDelegate? Write { get; }
    public MemberWriteDelegate? Initialize { get; }

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

    private static MemberReadDelegate BuildFieldGetter(FieldInfo field) {
        // Open generic declaring types or pointer/by-ref field types cannot
        // be used with expression trees. Fall back to FieldInfo.GetValue.
        if (field.DeclaringType?.ContainsGenericParameters == true
            || field.FieldType.IsPointer
            || field.FieldType.IsByRef)
            return (owner, _) => field.GetValue(field.IsStatic ? null : owner);

        var target = Expression.Parameter(typeof(object), "target");
        var args = Expression.Parameter(typeof(object?[]), "_");

        Expression fieldAccess = field.IsStatic
            ? Expression.Field(null, field)
            : Expression.Field(Expression.Convert(target, field.DeclaringType!), field);

        if (fieldAccess.Type.IsValueType)
            fieldAccess = Expression.Convert(fieldAccess, typeof(object));

        return Expression.Lambda<MemberReadDelegate>(fieldAccess, target, args).Compile();
    }

    private static MemberWriteDelegate BuildFieldSetter(FieldInfo field) {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        var args = Expression.Parameter(typeof(object?[]), "_");

        if (field.DeclaringType?.ContainsGenericParameters == true
            || field.FieldType.IsPointer
            || field.FieldType.IsByRef)
            return (owner, val, _) => {
                field.SetValue(field.IsStatic ? null : owner, val);
                return owner;
            };

        // Expression.Assign cannot write to readonly/const fields.
        // Fall back to FieldInfo.SetValue which bypasses the check.
        if (field.IsInitOnly || field.IsLiteral) {
            return (owner, val, _) => {
                field.SetValue(field.IsStatic ? null : owner, val);
                return owner;
            };
        }

        if (!field.IsStatic && field.DeclaringType!.IsValueType) {
            var unboxed = Expression.Variable(field.DeclaringType, "unboxed");
            var loadExpr = Expression.Assign(unboxed, Expression.Convert(target, field.DeclaringType));
            var fieldAssign = Expression.Assign(Expression.Field(unboxed, field), Expression.Convert(value, field.FieldType));
            return Expression.Lambda<MemberWriteDelegate>(
                Expression.Block([unboxed], loadExpr, fieldAssign, Expression.Convert(unboxed, typeof(object))),
                target, value, args).Compile();
        }

        Expression fieldAccess = Expression.Field(
            field.IsStatic ? null : Expression.Convert(target, field.DeclaringType!),
            field);

        return Expression.Lambda<MemberWriteDelegate>(
            Expression.Block(Expression.Assign(fieldAccess, Expression.Convert(value, field.FieldType)), target),
            target, value, args).Compile();
    }
}