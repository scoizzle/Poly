using System.Linq.Expressions;
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
            _read = BuildPropertyGetter(propertyInfo, getter);
        }

        var setter = propertyInfo.GetSetMethod(nonPublic: true);
        if (setter is not null) {
            if (IsInitOnlySetter(setter)) {
                _initialize = BuildPropertySetter(propertyInfo, setter);
                _write = null;
            }
            else {
                _write = BuildPropertySetter(propertyInfo, setter);
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

    private static MemberReadDelegate? BuildPropertyGetter(PropertyInfo property, MethodInfo getter) {
        var target = Expression.Parameter(typeof(object), "target");
        var args = Expression.Parameter(typeof(object?[]), "args");

        var paramInfos = getter.GetParameters();

        // Skip properties with ref/out/generic parameters — expression trees can't handle them.
        if (paramInfos.Any(p => p.ParameterType.IsByRef || p.ParameterType.ContainsGenericParameters))
            return BuildReflectionGetter(property, getter);

        var argExprs = paramInfos.Select((p, i) =>
            Expression.Convert(
                Expression.ArrayIndex(args, Expression.Constant(i)),
                p.ParameterType)).ToArray();

        try {
            return TryBuildGetter(getter, target, args, argExprs) ?? BuildReflectionGetter(property, getter);
        }
        catch {
            return BuildReflectionGetter(property, getter);
        }
    }

    private static MemberReadDelegate? TryBuildGetter(MethodInfo getter,
        ParameterExpression target, ParameterExpression args, Expression[] argExprs) {
        if (getter.ContainsGenericParameters)
            return null;

        Expression call;
        if (getter.IsStatic) {
            call = Expression.Call(getter, argExprs);
        }
        else if (getter.DeclaringType!.IsValueType) {
            var unboxed = Expression.Variable(getter.DeclaringType, "unboxed");
            var load = Expression.Assign(unboxed, Expression.Convert(target, getter.DeclaringType));
            var getterCall = Expression.Call(unboxed, getter, argExprs);
            call = Expression.Block([unboxed], load, getterCall);
        }
        else {
            call = Expression.Call(Expression.Convert(target, getter.DeclaringType!), getter, argExprs);
        }

        if (call.Type.IsValueType)
            call = Expression.Convert(call, typeof(object));

        return Expression.Lambda<MemberReadDelegate>(call, target, args).Compile();
    }

    private static MemberReadDelegate BuildReflectionGetter(PropertyInfo property, MethodInfo getter) {
        return (owner, arguments) => {
            var args = arguments is { Length: > 0 } ? arguments : null;
            return property.GetValue(getter.IsStatic ? null : owner, args);
        };
    }

    private static MemberWriteDelegate? BuildPropertySetter(PropertyInfo property, MethodInfo setter) {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        var args = Expression.Parameter(typeof(object?[]), "args");

        var paramInfos = setter.GetParameters();
        if (paramInfos.Any(p => p.ParameterType.IsByRef || p.ParameterType.ContainsGenericParameters))
            return BuildReflectionSetter(property, setter);

        var valueParam = paramInfos.Last();
        var indexParamInfos = paramInfos.Take(paramInfos.Length - 1);

        var callArgExprs = indexParamInfos.Select((p, i) => (Expression)
            Expression.Convert(
                Expression.ArrayIndex(args, Expression.Constant(i)),
                p.ParameterType)).ToList();
        callArgExprs.Add(Expression.Convert(value, valueParam.ParameterType));

        try {
            return TryBuildSetter(setter, target, value, args, callArgExprs) ?? BuildReflectionSetter(property, setter);
        }
        catch {
            return BuildReflectionSetter(property, setter);
        }
    }

    private static MemberWriteDelegate? TryBuildSetter(MethodInfo setter,
        ParameterExpression target, ParameterExpression value, ParameterExpression args, List<Expression> callArgs) {
        if (setter.ContainsGenericParameters)
            return null;

        if (setter.IsStatic) {
            return Expression.Lambda<MemberWriteDelegate>(
                Expression.Block(Expression.Call(setter, callArgs), target),
                target, value, args).Compile();
        }

        if (setter.DeclaringType!.IsValueType) {
            var unboxed = Expression.Variable(setter.DeclaringType, "unboxed");
            var load = Expression.Assign(unboxed, Expression.Convert(target, setter.DeclaringType));
            var setterCall = Expression.Call(unboxed, setter, callArgs);
            return Expression.Lambda<MemberWriteDelegate>(
                Expression.Block([unboxed], load, setterCall, Expression.Convert(unboxed, typeof(object))),
                target, value, args).Compile();
        }

        return Expression.Lambda<MemberWriteDelegate>(
            Expression.Block(
                Expression.Call(Expression.Convert(target, setter.DeclaringType!), setter, callArgs),
                target),
            target, value, args).Compile();
    }

    private static MemberWriteDelegate BuildReflectionSetter(PropertyInfo property, MethodInfo setter) {
        return (owner, val, arguments) => {
            var args = arguments is { Length: > 0 } ? arguments : null;
            setter.Invoke(setter.IsStatic ? null : owner, BuildSetterArgs(args, val));
            return owner;
        };
    }

    private static object?[] BuildSetterArgs(object?[]? indexArgs, object? value) {
        if (indexArgs is null || indexArgs.Length == 0)
            return [value];
        var result = new object?[indexArgs.Length + 1];
        Array.Copy(indexArgs, result, indexArgs.Length);
        result[^1] = value;
        return result;
    }
}