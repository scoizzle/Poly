using System.Linq.Expressions;
using System.Reflection;

using Poly.Interpretation.Vm;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// Analyzer that extracts ITypeDefinition instances from TypeDefinitionNode AST nodes.
/// Stores the extracted type definitions in the analysis context for use by other analyzers.
/// The table belongs to one analysis; query it through <see cref="Poly.Analysis.AnalysisResult.TypeDefinitions"/>.
/// </summary>
public sealed class TypeDefinitionNodeAnalyzer : INodeAnalyzer {
    public const string Id = "TypeDefinitionNode";
    public string PassName => Id;

    public void Analyze(AnalysisContext context, Node node) {
        var registry = context.GetMetadata<AstTypeRegistry>(default);
        if (registry is null) {
            registry = new AstTypeRegistry();
            context.SetMetadata(default, registry);
            context.TypeDefinitions.Add(registry);
        }

        if (node is TypeDefinitionNode typeDef) {
            // A re-analysis of a type the caller already built must keep that
            // instance. A second Patron is not assignable to the first, so
            // this fails to match a Patron parameter. A type registered by the
            // provider passed to this analysis is that same instance. Look up
            // by node, not name: a domain can hold two definitions with the
            // same name (module and runtime shapes), and the provider's
            // precedence decides which one a bare name resolves to.
            var existing = context.GetMetadata<TypeDefinitionMetadata>(typeDef)?.TypeDefinition
                    as AstTypeDefinition
                ?? context.TypeDefinitions.GetTypeDefinition(typeDef) as AstTypeDefinition;
            if (existing is not null) {
                if (context.GetMetadata<TypeDefinitionMetadata>(typeDef) is null)
                    context.SetMetadata(typeDef, new TypeDefinitionMetadata(existing));
            }
            else {
                var definition = new AstTypeDefinition(typeDef, context.TypeDefinitions);
                registry.Add(definition);
                context.SetMetadata(node, new TypeDefinitionMetadata(definition));
            }
        }

        this.AnalyzeChildren(context, node);
    }
}

/// <summary>
/// Per-analysis AST type table. Lives on <see cref="AnalysisContext"/> so the
/// shared <see cref="Interpreter.Analyzer"/> instance cannot leak or clear
/// types across concurrent analyses.
/// </summary>
internal sealed class AstTypeRegistry : ITypeDefinitionProvider, ITypeDefinitionNodeProvider, IAnalysisMetadata {
    private readonly Dictionary<string, AstTypeDefinition> _types = new(StringComparer.Ordinal);

    /// <summary>CLR arity suffix so <c>DomainResult</c> and <c>DomainResult&lt;T&gt;</c> both stay registered.</summary>
    internal static string Key(string fullName, int arity) =>
        arity <= 0 ? fullName : $"{fullName}`{arity}";

    public void Add(AstTypeDefinition definition) {
        _types[Key(definition.FullName, definition.GenericParameters.Count())] = definition;
    }

    public IEnumerable<ITypeDefinition> GetTypeDefinitions() => _types.Values;

    /// <summary>The declarations this analysis registered, so a compile can put
    /// them and the root in one unit.</summary>
    public IEnumerable<TypeDefinitionNode> GetTypeDefinitionNodes() =>
        _types.Values.Select(static type => type.Syntax);

    /// <summary>The definition built for exactly <paramref name="node"/>, or null.
    /// Node identity, not name: a domain can hold two definitions with the same
    /// name (module and runtime shapes).</summary>
    public ITypeDefinition? GetTypeDefinition(TypeDefinitionNode node) =>
        _types.Values.FirstOrDefault(type => ReferenceEquals(type.Syntax, node));

    public ITypeDefinition? GetTypeDefinition(string typeName) {
        if (_types.TryGetValue(typeName, out var def))
            return def;

        var arity = TryReadArity(typeName, out var bareName);
        AstTypeDefinition? match = null;
        foreach (var type in _types.Values) {
            if (!string.Equals(type.Name, bareName, StringComparison.Ordinal)
                && !string.Equals(type.FullName, bareName, StringComparison.Ordinal))
                continue;
            if (arity is int required && type.GenericParameters.Count() != required)
                continue;
            if (match is not null)
                return null;
            match = type;
        }
        return match;
    }

    private static int? TryReadArity(string typeName, out string bareName) {
        var tick = typeName.LastIndexOf('`');
        if (tick > 0
            && int.TryParse(typeName.AsSpan(tick + 1), out var parsed)
            && parsed >= 0) {
            bareName = typeName[..tick];
            return parsed;
        }

        bareName = typeName;
        return null;
    }

    public ITypeDefinition? GetTypeDefinition(Type type) =>
        ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);
}

/// <summary>Metadata associating a <see cref="TypeDefinitionNode"/> with its
/// resolved <see cref="ITypeDefinition"/>. Set by
/// <see cref="TypeDefinitionNodeAnalyzer"/> during analysis.</summary>
/// <param name="TypeDefinition">The resolved type definition.</param>
public sealed record TypeDefinitionMetadata(ITypeDefinition TypeDefinition) : IAnalysisMetadata;

public static class TypeDefinitionNodeAnalyzerExtensions {
    public static AnalyzerBuilder UseTypeDefinitionNodeAnalyzer(this AnalyzerBuilder builder) {
        builder.AddAnalyzer(new TypeDefinitionNodeAnalyzer());
        return builder;
    }
}

/// <summary>
/// ITypeDefinition implementation backed by a TypeDefinitionNode AST.
/// </summary>
internal sealed class AstTypeDefinition : ITypeDefinition, IClrTypeDefinition {
    private readonly TypeDefinitionNode _node;
    private readonly ITypeDefinitionProvider _provider;
    private readonly Lazy<ITypeDefinition?> _baseType;
    private readonly Lazy<IReadOnlyList<ITypeDefinition>> _interfaces;
    private readonly Lazy<IReadOnlyList<IParameter>> _genericParameters;
    private readonly Lazy<IReadOnlyList<AstConstructorDefinition>> _constructors;
    private readonly Lazy<IReadOnlyList<AstPropertyDefinition>> _declaredProperties;
    private readonly Lazy<IReadOnlyList<AstMethodDefinition>> _declaredMethods;
    private readonly Lazy<IReadOnlyList<AstFieldDefinition>> _declaredFields;
    private readonly Lazy<IReadOnlyList<ITypeProperty>> _properties;
    private readonly Lazy<IReadOnlyList<ITypeMethod>> _methods;
    private readonly Lazy<IReadOnlyList<ITypeField>> _fields;
    private readonly Lazy<IReadOnlyList<ITypeMember>> _members;

    public AstTypeDefinition(TypeDefinitionNode node, ITypeDefinitionProvider provider) {
        _node = node;
        _provider = provider;
        _baseType = new(() => _node.BaseType is null ? null : ResolveType(_node.BaseType));
        _interfaces = new(() => _node.Interfaces?.Select(ResolveType).ToArray() ?? []);
        _genericParameters = new(() => MapParameters(_node.GenericParameters));
        _constructors = new(() => BuildConstructors());
        _declaredProperties = new(() => BuildDeclaredProperties());
        _declaredMethods = new(() => BuildDeclaredMethods());
        _declaredFields = new(() => BuildDeclaredFields());
        _properties = new(() => BuildProperties());
        _methods = new(() => BuildMethods());
        _fields = new(() => BuildFields());
        _members = new(() => [.. Constructors, .. Properties, .. Methods, .. Fields]);
    }

    internal TypeDefinitionNode Syntax => _node;

    /// <summary>Provider this type was resolved against, including sibling AST types.</summary>
    internal ITypeDefinitionProvider ResolutionProvider => _provider;

    public string Name => _node.Name;
    public string? Namespace => _node.Namespace;
    public string FullName => _node.FullName;
    public AccessModifier AccessModifier => _node.AccessModifier;

    public IEnumerable<ITypeMember> Members => _members.Value;

    public IEnumerable<ITypeField> Fields => _fields.Value;
    public IEnumerable<ITypeProperty> Properties => _properties.Value;
    public IEnumerable<ITypeMethod> Methods => _methods.Value;
    public IEnumerable<ITypeConstructor> Constructors => _constructors.Value;

    // AST-based types are dictionary-backed at runtime
    public Type RuntimeType => typeof(IDictionary<string, object>);

    public ITypeDefinition? BaseType => _baseType.Value;
    public IEnumerable<ITypeDefinition> Interfaces => _interfaces.Value;
    public IEnumerable<IParameter> GenericParameters => _genericParameters.Value;

    public PrimitiveType? PrimitiveType => _node.PrimitiveTypeId;
    public TypeCategory TypeCategory => _node.TypeCategory;

    private List<AstPropertyDefinition> BuildDeclaredProperties() {
        var properties = new List<PropertyDefinitionNode>();

        if (_node.Properties is not null) {
            properties.AddRange(_node.Properties);
        }

        if (_node.PrimaryConstructorParameters is not null) {
            var explicitPropertyNames = new HashSet<string>(properties.Select(static property => property.Name), StringComparer.Ordinal);
            foreach (var parameter in _node.PrimaryConstructorParameters) {
                if (parameter.TypeReference is null || !explicitPropertyNames.Add(parameter.Name)) {
                    continue;
                }

                properties.Add(new PropertyDefinitionNode(
                    parameter.Name,
                    parameter.TypeReference,
                    Getter: new PropertyGetterDefinitionNode()));
            }
        }

        return [.. properties.Select(p => new AstPropertyDefinition(p, this))];
    }

    private List<AstConstructorDefinition> BuildConstructors() {
        var constructors = new List<ConstructorDefinitionNode>();

        if (_node.PrimaryConstructorParameters is { Count: > 0 }) {
            constructors.Add(new ConstructorDefinitionNode(_node.PrimaryConstructorParameters));
        }

        if (_node.Constructors is not null) {
            constructors.AddRange(_node.Constructors);
        }

        return [.. constructors.Select(constructor => new AstConstructorDefinition(constructor, this))];
    }

    private List<AstMethodDefinition> BuildDeclaredMethods() {
        return _node.Methods?
            .Select(m => new AstMethodDefinition(m, this))
            .ToList() ?? [];
    }

    private List<AstFieldDefinition> BuildDeclaredFields() {
        return _node.Fields?
            .Select(f => new AstFieldDefinition(f, this))
            .ToList() ?? [];
    }

    private List<ITypeProperty> BuildProperties() {
        return ComposeInheritedMembers(
            _declaredProperties.Value,
            static typeDefinition => typeDefinition.Properties,
            static property => $"{property.Name}|{property.LifetimeModifier}|{GetParameterSignature(property.Parameters)}");
    }

    private List<ITypeMethod> BuildMethods() {
        return ComposeInheritedMembers(
            _declaredMethods.Value,
            static typeDefinition => typeDefinition.Methods,
            static method => $"{method.Name}|{method.LifetimeModifier}|{GetParameterSignature(method.Parameters)}");
    }

    private List<ITypeField> BuildFields() {
        return ComposeInheritedMembers(
            _declaredFields.Value,
            static typeDefinition => typeDefinition.Fields,
            static field => $"{field.Name}|{field.LifetimeModifier}");
    }

    private List<TMember> ComposeInheritedMembers<TMember>(
        IEnumerable<TMember> declaredMembers,
        Func<ITypeDefinition, IEnumerable<TMember>> inheritedMemberSelector,
        Func<TMember, string> keySelector) {
        ArgumentNullException.ThrowIfNull(declaredMembers);
        ArgumentNullException.ThrowIfNull(inheritedMemberSelector);
        ArgumentNullException.ThrowIfNull(keySelector);

        var members = new List<TMember>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        AddMembers(declaredMembers);

        if (BaseType is not null) {
            AddMembers(inheritedMemberSelector(BaseType));
        }

        foreach (var implementedInterface in Interfaces) {
            AddMembers(inheritedMemberSelector(implementedInterface));
        }

        return members;

        void AddMembers(IEnumerable<TMember> source) {
            foreach (var member in source) {
                if (seen.Add(keySelector(member))) {
                    members.Add(member);
                }
            }
        }
    }

    private static string GetParameterSignature(IEnumerable<IParameter> parameters) {
        return string.Join(",", parameters.Select(static parameter => parameter.ParameterTypeDefinition.FullName));
    }

    internal ITypeDefinition ResolveType(Node typeNode) =>
        AstTypeReferenceResolver.Resolve(typeNode, _provider, this);

    internal IReadOnlyList<IParameter> MapParameters(IReadOnlyList<Parameter>? parameters) {
        return parameters?
            .Select((parameter, index) => new AstParameterDefinition(parameter, index, this))
            .Cast<IParameter>()
            .ToArray() ?? [];
    }
}

internal sealed class AstConstructorDefinition : ITypeConstructor {
    private readonly ConstructorDefinitionNode _node;
    private readonly AstTypeDefinition _declaringType;
    private readonly Lazy<IReadOnlyList<IParameter>> _parameters;

    public AstConstructorDefinition(ConstructorDefinitionNode node, AstTypeDefinition declaringType) {
        _node = node;
        _declaringType = declaringType;
        _parameters = new(() => _declaringType.MapParameters(_node.Parameters));
    }

    internal ConstructorDefinitionNode Definition => _node;

    public string Name => _declaringType.Name;
    public ITypeDefinition MemberTypeDefinition => _declaringType;
    public ITypeDefinition DeclaringTypeDefinition => _declaringType;
    public IEnumerable<IParameter> Parameters => _parameters.Value;
    public AccessModifier AccessModifier => _node.AccessModifier;
    public LifetimeModifier LifetimeModifier => LifetimeModifier.Instance;

    public Mutability Mutability => Mutability.Mutable;
}

internal sealed class AstParameterDefinition : IParameter {
    private readonly Parameter _node;
    private readonly AstTypeDefinition _declaringType;
    private readonly Lazy<ITypeDefinition> _parameterType;
    private readonly Lazy<object?> _defaultValue;

    public AstParameterDefinition(Parameter node, int position, AstTypeDefinition declaringType) {
        _node = node;
        _declaringType = declaringType;
        Position = position;
        _parameterType = new(() => _node.TypeReference is null
            ? ClrTypeDefinitionRegistry.Shared.GetTypeDefinition<object>()
            : _declaringType.ResolveType(_node.TypeReference));
        _defaultValue = new(() => _node.DefaultValue is Constant constant ? constant.Value : null);
    }

    public int Position { get; }
    public string Name => _node.Name;
    public ITypeDefinition ParameterTypeDefinition => _parameterType.Value;
    public bool IsOptional => _node.DefaultValue is not null;
    public object? DefaultValue => _defaultValue.Value;
}

/// <summary>
/// ITypeProperty implementation backed by a PropertyDefinitionNode AST.
/// At runtime the declaring type is dictionary-backed (IDictionary&lt;string, object&gt;),
/// so the Read delegate indexes into the dictionary by property name.
/// </summary>
internal sealed class AstPropertyDefinition(PropertyDefinitionNode node, AstTypeDefinition declaring) : ITypeProperty {
    private readonly PropertyDefinitionNode _node = node;
    private readonly AstTypeDefinition _declaring = declaring;
    private readonly Lazy<ITypeDefinition> _memberType = new(() => declaring.ResolveType(node.MemberType));

    public string Name => _node.Name;
    public ITypeDefinition MemberTypeDefinition => _memberType.Value;
    public ITypeDefinition DeclaringTypeDefinition => _declaring;

    internal ITypeDefinition? TryGetCollectionElementType() =>
        _node.MemberType is CollectionTypeReference col
            ? _declaring.ResolveType(col.ElementType)
            : null;

    public IEnumerable<IParameter> Parameters => _node.IndexParameters is null ? [] : _declaring.MapParameters(_node.IndexParameters);

    public AccessModifier AccessModifier => _node.AccessModifier;
    public LifetimeModifier LifetimeModifier => _node.IsStatic ? LifetimeModifier.Static : LifetimeModifier.Instance;
    public bool IsStatic => _node.IsStatic;

    public Mutability Mutability => _node.Mutability;

    /// <summary>
    /// Emits an expression that reads this property from an <c>IDictionary&lt;string, object?&gt;</c>
    /// using the indexer. Coerces the stored value to the property's target type.
    /// Returns the property's <c>DefaultValue</c> when the key is not present.
    /// A value-type member with no default reads as that type's zero value.
    /// A reference member with no default reads as <see cref="System.Reflection.Missing.Value"/>.
    /// </summary>
    public Expression? EmitRead(Expression? instance) {
        if (instance is null) return null;
        var typed = Expression.Convert(instance, typeof(IDictionary<string, object?>));
        var rawValue = Expression.Call(typed, DictionaryBackedValue.DictGetItem, Expression.Constant(Name));
        var fallback = _node.DefaultValue is Constant c
            ? Expression.Convert(Expression.Constant(c.Value), typeof(object))
            : DictionaryBackedValue.MissingKeyFallback(MemberTypeDefinition);
        var value = DictionaryBackedValue.CoerceRead(rawValue, MemberTypeDefinition);
        return Expression.Condition(
            Expression.Call(typed, DictionaryBackedValue.DictContainsKey, Expression.Constant(Name)),
            value,
            fallback);
    }

    public Expression? EmitWrite(Expression? instance, Expression value) {
        if (instance is null) return null;
        var typed = Expression.Convert(instance, typeof(IDictionary<string, object?>));
        return Expression.Block(
            Expression.Call(typed, DictionaryBackedValue.DictSetItem, Expression.Constant(Name), value),
            instance);
    }
}

/// <summary>
/// ITypeMethod implementation backed by a MethodDefinitionNode AST.
/// </summary>
internal sealed class AstMethodDefinition(MethodDefinitionNode node, AstTypeDefinition declaring) : ITypeMethod {
    private readonly MethodDefinitionNode _node = node;
    private readonly AstTypeDefinition _declaring = declaring;
    private readonly Lazy<ITypeDefinition> _returnType = new(() => declaring.ResolveType(node.ReturnType));

    public MethodDefinitionNode DefinitionNode => _node;
    public string Name => _node.Name;
    public ITypeDefinition MemberTypeDefinition => _returnType.Value;
    public ITypeDefinition DeclaringTypeDefinition => _declaring;
    public IEnumerable<IParameter> Parameters => _declaring.MapParameters(_node.Parameters);
    public AccessModifier AccessModifier => _node.AccessModifier;
    public LifetimeModifier LifetimeModifier => _node.IsStatic ? LifetimeModifier.Static : LifetimeModifier.Instance;
    public bool IsStatic => _node.IsStatic;

    public Mutability Mutability => Mutability.Mutable;
}

/// <summary>
/// ITypeField implementation backed by a FieldDefinitionNode AST.
/// </summary>
internal sealed class AstFieldDefinition(FieldDefinitionNode node, AstTypeDefinition declaring) : ITypeField {
    private readonly FieldDefinitionNode _node = node;
    private readonly AstTypeDefinition _declaring = declaring;
    private readonly Lazy<ITypeDefinition> _fieldType = new(() => declaring.ResolveType(node.FieldType));

    public string Name => _node.Name;
    public ITypeDefinition MemberTypeDefinition => _fieldType.Value;
    public ITypeDefinition DeclaringTypeDefinition => _declaring;
    public IEnumerable<IParameter> Parameters => [];

    public AccessModifier AccessModifier => _node.AccessModifier;
    public LifetimeModifier LifetimeModifier => _node.IsStatic ? LifetimeModifier.Static : LifetimeModifier.Instance;
    public bool IsStatic => _node.IsStatic;

    public Mutability Mutability => _node.Mutability;

    public Expression? EmitRead(Expression? instance) {
        if ((_node.IsStatic || _node.IsConst) && _node.DefaultValue is Constant constant)
            return Expression.Constant(constant.Value, typeof(object));
        if (instance is null) return null;
        var typed = Expression.Convert(instance, typeof(IDictionary<string, object?>));
        var rawValue = Expression.Call(typed, DictionaryBackedValue.DictGetItem, Expression.Constant(Name));
        var fallback = _node.DefaultValue is Constant c
            ? Expression.Convert(Expression.Constant(c.Value), typeof(object))
            : DictionaryBackedValue.MissingKeyFallback(MemberTypeDefinition);
        var value = DictionaryBackedValue.CoerceRead(rawValue, MemberTypeDefinition);
        return Expression.Condition(
            Expression.Call(typed, DictionaryBackedValue.DictContainsKey, Expression.Constant(Name)),
            value,
            fallback);
    }

    public Expression? EmitWrite(Expression? instance, Expression value) {
        if (instance is null) return null;
        var typed = Expression.Convert(instance, typeof(IDictionary<string, object?>));
        return Expression.Block(
            Expression.Call(typed, DictionaryBackedValue.DictSetItem, Expression.Constant(Name), value),
            instance);
    }
}

/// <summary>
/// Helper for reading/writing values from dictionary-backed AST type instances.
/// Provides type coercion so stored values (e.g. <c>int</c>) are correctly
/// converted to the declared member type (e.g. <c>long</c> for Number).
/// </summary>
internal static class DictionaryBackedValue {
    internal static readonly MethodInfo DictContainsKey =
        Ref<IDictionary<string, object?>>.Method(d => d.ContainsKey(""));
    private static readonly PropertyInfo DictItem =
        Ref<IDictionary<string, object?>>.Indexer(d => d[""]);
    internal static readonly MethodInfo DictGetItem = DictItem.GetGetMethod()!;
    internal static readonly MethodInfo DictSetItem = DictItem.GetSetMethod()!;

    private static readonly MethodInfo ConvertToInt64 =
        Ref.Method((Expression<Func<object, long>>)(o => Convert.ToInt64(o)));
    private static readonly MethodInfo ConvertToInt32 =
        Ref.Method((Expression<Func<object, int>>)(o => Convert.ToInt32(o)));
    private static readonly MethodInfo ConvertToInt16 =
        Ref.Method((Expression<Func<object, short>>)(o => Convert.ToInt16(o)));
    private static readonly MethodInfo ConvertToSByte =
        Ref.Method((Expression<Func<object, sbyte>>)(o => Convert.ToSByte(o)));
    private static readonly MethodInfo ConvertToDouble =
        Ref.Method((Expression<Func<object, double>>)(o => Convert.ToDouble(o)));
    private static readonly MethodInfo ConvertToSingle =
        Ref.Method((Expression<Func<object, float>>)(o => Convert.ToSingle(o)));
    private static readonly MethodInfo ConvertToDecimal =
        Ref.Method((Expression<Func<object, decimal>>)(o => Convert.ToDecimal(o)));
    private static readonly MethodInfo ConvertToBoolean =
        Ref.Method((Expression<Func<object, bool>>)(o => Convert.ToBoolean(o)));
    private static readonly MethodInfo ConvertToString =
        Ref.Method((Expression<Func<object, string?>>)(o => Convert.ToString(o)));
    private static readonly MethodInfo ConvertToChar =
        Ref.Method((Expression<Func<object, char>>)(o => Convert.ToChar(o)));
    private static readonly MethodInfo GuardCompatibleInfo =
        Ref.Method((Expression<Func<object?, PrimitiveType, object>>)((raw, t) => GuardCompatible(raw, t)));

    /// <summary>
    /// Value used when a dictionary key is absent and the member has no constant
    /// default. A converted value type uses that conversion's zero.
    /// <see cref="System.Reflection.Missing"/> cannot be the other branch of a
    /// value-type read: unboxing the condition then casts the zero to <c>Missing</c>.
    /// </summary>
    internal static Expression MissingKeyFallback(ITypeDefinition targetType) {
        // An unset optional value is null. Nullable<T> has no primitive, so the
        // value-type zero below would not apply and Missing cannot convert to T?.
        if (targetType.GetRuntimeType() is { } runtime && Nullable.GetUnderlyingType(runtime) is not null)
            return Expression.Constant(null, typeof(object));
        var convert = ConvertMethod(targetType.PrimitiveType);
        if (convert is null || !convert.ReturnType.IsValueType)
            return Expression.Convert(Expression.Constant(System.Reflection.Missing.Value), typeof(object));
        var zero = Activator.CreateInstance(convert.ReturnType)!;
        return Expression.Convert(Expression.Constant(zero), typeof(object));
    }

    private static MethodInfo? ConvertMethod(PrimitiveType? primitive) => primitive switch {
        PrimitiveType.Int64 => ConvertToInt64,
        PrimitiveType.Int32 => ConvertToInt32,
        PrimitiveType.Int16 => ConvertToInt16,
        PrimitiveType.Int8 => ConvertToSByte,
        PrimitiveType.Float64 => ConvertToDouble,
        PrimitiveType.Float32 => ConvertToSingle,
        PrimitiveType.Decimal => ConvertToDecimal,
        PrimitiveType.Boolean => ConvertToBoolean,
        PrimitiveType.String => ConvertToString,
        PrimitiveType.Char => ConvertToChar,
        _ => null
    };

    internal static Expression CoerceRead(Expression dictValue, ITypeDefinition targetType) {
        var convertMethod = ConvertMethod(targetType.PrimitiveType);
        if (convertMethod is not null) {
            // Fail loud on fundamentally wrong-typed raw values instead of silently
            // coercing (Convert.ToInt64(true) → 1, Convert.ToString(null) → "", etc.):
            // a property bag holding a bool for a Number prop (or a non-numeric string)
            // must surface as an error, not a silently-mangled value.
            var guarded = Expression.Call(
                GuardCompatibleInfo,
                Expression.Convert(dictValue, typeof(object)),
                Expression.Constant(targetType.PrimitiveType!.Value));
            return Expression.Convert(
                Expression.Call(null, convertMethod, guarded),
                typeof(object));
        }

        return dictValue;
    }

    private static bool IsIntegralNumber(object raw) => raw switch {
        double d => d == Math.Truncate(d) && !double.IsNaN(d) && !double.IsInfinity(d),
        float f => f == Math.Truncate(f) && !float.IsNaN(f) && !float.IsInfinity(f),
        decimal m => m == decimal.Truncate(m),
        _ => true
    };

    /// <summary>
    /// Validates a raw bag value against the target primitive before coercion. Rejects
    /// values that Convert.* would silently mangle (bool/object → number, null → default),
    /// so the runtime fails loud instead of storing a corrupted value.
    /// </summary>
    internal static object GuardCompatible(object? raw, PrimitiveType target) {
        if (raw is null) return null!;
        switch (target) {
            case PrimitiveType.Int64 or PrimitiveType.Int32 or PrimitiveType.Int16 or PrimitiveType.Int8
                or PrimitiveType.Float64 or PrimitiveType.Float32 or PrimitiveType.Decimal:
                if (raw is bool)
                    throw new InvalidOperationException(
                        $"Cannot store a Boolean value in a numeric property (got '{raw}').");
                if (raw is not (long or int or short or byte or sbyte or ushort or uint or ulong or double or float or decimal))
                    throw new InvalidOperationException(
                        $"Cannot store a value of type '{raw.GetType().Name}' in a numeric property.");
                // Number is long. Convert.ToInt64 would truncate 0.25 to 0.
                if (target == PrimitiveType.Int64 && raw is double or float or decimal && !IsIntegralNumber(raw))
                    throw new InvalidOperationException(
                        $"Number is integral; '{raw}' is not a valid Number.");
                return raw;
            case PrimitiveType.Boolean:
                if (raw is not bool)
                    throw new InvalidOperationException(
                        $"Cannot store a value of type '{raw.GetType().Name}' in a Boolean property.");
                return raw;
            case PrimitiveType.String:
                if (raw is not string)
                    throw new InvalidOperationException(
                        $"Cannot store a value of type '{raw.GetType().Name}' in a Text property.");
                return raw;
            case PrimitiveType.Char:
                if (raw is not char and not string)
                    throw new InvalidOperationException(
                        $"Cannot store a value of type '{raw.GetType().Name}' in a character property.");
                return raw;
            default:
                return raw;
        }
    }

    /// <summary>
    /// Runtime counterpart of <see cref="CoerceRead"/>: GuardCompatible, then
    /// the same Convert.* as the member-read expression.
    /// </summary>
    internal static object? CoerceReadValue(object? raw, PrimitiveType target) {
        var guarded = GuardCompatible(raw, target);
        return target switch {
            PrimitiveType.Int64 => Convert.ToInt64(guarded),
            PrimitiveType.Int32 => Convert.ToInt32(guarded),
            PrimitiveType.Int16 => Convert.ToInt16(guarded),
            PrimitiveType.Int8 => Convert.ToSByte(guarded),
            PrimitiveType.Float64 => Convert.ToDouble(guarded),
            PrimitiveType.Float32 => Convert.ToSingle(guarded),
            PrimitiveType.Decimal => Convert.ToDecimal(guarded),
            PrimitiveType.Boolean => Convert.ToBoolean(guarded),
            PrimitiveType.String => Convert.ToString(guarded),
            PrimitiveType.Char => Convert.ToChar(guarded),
            _ => guarded
        };
    }
}


/// <summary>
/// Utility class to resolve AST type reference nodes to ITypeDefinition.
/// </summary>
internal static class AstTypeReferenceResolver {
    public static ITypeDefinition Resolve(
        Node typeNode,
        ITypeDefinitionProvider provider,
        ITypeDefinition? enclosing = null) {
        return TryResolve(typeNode, provider, enclosing)
            ?? throw new InvalidOperationException(
                $"Type with name '{TypeNameOf(typeNode)}' not found.");
    }

    public static ITypeDefinition? TryResolve(
        Node typeNode,
        ITypeDefinitionProvider provider,
        ITypeDefinition? enclosing = null) {
        var clr = ClrTypeDefinitionRegistry.Shared;

        return typeNode switch {
            PrimitiveTypeReference prim => ResolvePrimitive(prim.PrimitiveId, prim.IsNullable, clr),
            NamedTypeReference named => ResolveNamed(named, provider, clr, enclosing),
            OptionalTypeReference opt => ResolveOptional(opt, provider, clr, enclosing),
            CollectionTypeReference col => ResolveCollection(col, provider, clr, enclosing),
            MapTypeReference map => ResolveMap(map, provider, clr, enclosing),
            UnionTypeReference union => ResolveUnion(union, provider, clr, enclosing),
            TypeDefinitionReference tdr => tdr.TypeDefinition,
            ClrTypeReference clrRef => provider.GetTypeDefinition(clrRef.RuntimeType) ?? clr.GetTypeDefinition<object>(),
            TypeReference tr => ResolveByName(tr.TypeName, provider, clr),
            _ => clr.GetTypeDefinition<object>()
        };
    }

    private static string TypeNameOf(Node typeNode) => typeNode switch {
        NamedTypeReference named => named.FullName,
        TypeReference tr => tr.TypeName,
        _ => typeNode.ToString() ?? typeNode.GetType().Name
    };

    private static ClrTypeDefinition ResolvePrimitive(PrimitiveType id, bool isNullable, ClrTypeDefinitionRegistry clr) {
        var baseType = id switch {
            PrimitiveType.Boolean => clr.GetTypeDefinition<bool>(),
            PrimitiveType.Int8 => clr.GetTypeDefinition<sbyte>(),
            PrimitiveType.Int16 => clr.GetTypeDefinition<short>(),
            PrimitiveType.Int32 => clr.GetTypeDefinition<int>(),
            PrimitiveType.Int64 => clr.GetTypeDefinition<long>(),
            PrimitiveType.UInt8 => clr.GetTypeDefinition<byte>(),
            PrimitiveType.UInt16 => clr.GetTypeDefinition<ushort>(),
            PrimitiveType.UInt32 => clr.GetTypeDefinition<uint>(),
            PrimitiveType.UInt64 => clr.GetTypeDefinition<ulong>(),
            PrimitiveType.Float32 => clr.GetTypeDefinition<float>(),
            PrimitiveType.Float64 => clr.GetTypeDefinition<double>(),
            PrimitiveType.Decimal => clr.GetTypeDefinition<decimal>(),
            PrimitiveType.String => clr.GetTypeDefinition<string>(),
            PrimitiveType.Char => clr.GetTypeDefinition<char>(),
            PrimitiveType.DateTime => clr.GetTypeDefinition<DateTime>(),
            PrimitiveType.DateOnly => clr.GetTypeDefinition<DateOnly>(),
            PrimitiveType.TimeOnly => clr.GetTypeDefinition<TimeOnly>(),
            PrimitiveType.TimeSpan => clr.GetTypeDefinition<TimeSpan>(),
            PrimitiveType.Guid => clr.GetTypeDefinition<Guid>(),
            PrimitiveType.ByteArray => clr.GetTypeDefinition<byte[]>(),
            PrimitiveType.Structure => clr.GetTypeDefinition<object>(),
            _ => clr.GetTypeDefinition<object>()
        };

        if (isNullable && baseType.RuntimeType.IsValueType) {
            var nullableType = typeof(Nullable<>).MakeGenericType(baseType.RuntimeType);
            return clr.GetTypeDefinition(nullableType);
        }

        return baseType;
    }

    private static ITypeDefinition? ResolveNamed(
        NamedTypeReference named,
        ITypeDefinitionProvider provider,
        ClrTypeDefinitionRegistry clr,
        ITypeDefinition? enclosing) {
        if (named.TypeArguments is { Count: > 0 } args) {
            var resolvedArgs = new ITypeDefinition[args.Count];
            ITypeDefinition? open = null;
            for (var i = 0; i < args.Count; i++) {
                var resolved = TryResolve(args[i], provider, enclosing);
                if (resolved is null && !TryCollectionKind(named.TypeName, out _)) {
                    // DomainResult<T> inside the open definition: T is that definition's parameter,
                    // not a separate type. An unknown name still fails.
                    open ??= LookupOpenGeneric(named, args.Count, provider);
                    resolved = open is null ? null : TypeParameterOf(open, args[i]);
                }
                if (resolved is null)
                    return null;
                resolvedArgs[i] = resolved;
            }
            return CloseNamed(named, resolvedArgs, provider, clr, enclosing);
        }

        return LookupNamed(named, provider, clr, enclosing, allowTypeParameter: true);
    }

    private static ITypeDefinition? LookupNamed(
        NamedTypeReference named,
        ITypeDefinitionProvider provider,
        ClrTypeDefinitionRegistry clr,
        ITypeDefinition? enclosing,
        bool allowTypeParameter) {
        if (named.Namespace is null
            && string.Equals(named.TypeName, "void", StringComparison.OrdinalIgnoreCase))
            return clr.GetTypeDefinition(typeof(void));

        if (allowTypeParameter && enclosing is not null && named.Namespace is null) {
            foreach (var genericParameter in enclosing.GenericParameters) {
                if (string.Equals(genericParameter.Name, named.TypeName, StringComparison.Ordinal))
                    return new AstGenericParameterTypeDefinition(genericParameter, enclosing);
            }
        }

        return provider.GetTypeDefinition(named.FullName)
            ?? (named.Namespace is not null ? provider.GetTypeDefinition(named.TypeName) : null)
            ?? clr.GetTypeDefinition(named.FullName)
            ?? (named.FullName != named.TypeName ? clr.GetTypeDefinition(named.TypeName) : null);
    }

    private static ITypeDefinition? CloseNamed(
        NamedTypeReference named,
        ITypeDefinition[] args,
        ITypeDefinitionProvider provider,
        ClrTypeDefinitionRegistry clr,
        ITypeDefinition? enclosing) {
        if (args.Length == 1 && TryCollectionKind(named.TypeName, out var kind))
            return CloseCollection(args[0], kind, clr);

        var open = LookupOpenGeneric(named, args.Length, provider)
            ?? LookupNamed(named, provider, clr, enclosing, allowTypeParameter: false);
        if (open is null)
            return null;
        return CloseGeneric(open, args, clr);
    }

    private static ITypeDefinition? TypeParameterOf(ITypeDefinition open, Node argument) {
        if (argument is not NamedTypeReference named || named.Namespace is not null || named.TypeArguments is { Count: > 0 })
            return null;
        foreach (var parameter in open.GenericParameters) {
            if (string.Equals(parameter.Name, named.TypeName, StringComparison.Ordinal))
                return new AstGenericParameterTypeDefinition(parameter, open);
        }
        return null;
    }

    private static ITypeDefinition? LookupOpenGeneric(
        NamedTypeReference named,
        int arity,
        ITypeDefinitionProvider provider) {
        if (arity <= 0)
            return null;

        // Arity keys are an AST-registry fact. A CLR hit on `Name`1` (a loaded
        // DomainResult<T>) must not steal the call from the non-generic runtime type.
        var fullKey = AstTypeRegistry.Key(named.FullName, arity);
        if (provider.GetTypeDefinition(fullKey) is AstTypeDefinition full)
            return full;
        if (named.FullName == named.TypeName)
            return null;

        var shortKey = AstTypeRegistry.Key(named.TypeName, arity);
        return provider.GetTypeDefinition(shortKey) as AstTypeDefinition;
    }

    private static bool TryCollectionKind(string typeName, out CollectionKind kind) {
        switch (typeName) {
            case "List":
            case "IList":
            case "ICollection":
            case "IEnumerable":
            case "IReadOnlyList":
            case "IReadOnlyCollection":
                kind = CollectionKind.List;
                return true;
            case "HashSet":
            case "ISet":
                kind = CollectionKind.Set;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static ITypeDefinition CloseCollection(
        ITypeDefinition element,
        CollectionKind kind,
        ClrTypeDefinitionRegistry clr) {
        if (element is AstTypeDefinition or AstCollectionTypeDefinition or AstGenericParameterTypeDefinition)
            return new AstCollectionTypeDefinition(element, kind);

        var elementClrType = element.GetRuntimeTypeOrThrow();
        var collectionClrType = kind switch {
            CollectionKind.Array => elementClrType.MakeArrayType(),
            CollectionKind.List => typeof(List<>).MakeGenericType(elementClrType),
            CollectionKind.Set => typeof(HashSet<>).MakeGenericType(elementClrType),
            _ => typeof(IEnumerable<>).MakeGenericType(elementClrType)
        };
        return clr.GetTypeDefinition(collectionClrType);
    }

    private static ITypeDefinition CloseGeneric(
        ITypeDefinition open,
        ITypeDefinition[] args,
        ClrTypeDefinitionRegistry clr) {
        if (open is not IClrTypeDefinition clrOpen)
            return open;

        var runtimeType = clrOpen.RuntimeType;
        var definition = runtimeType.IsGenericTypeDefinition
            ? runtimeType
            : runtimeType.IsGenericType ? runtimeType.GetGenericTypeDefinition() : null;
        if (definition is null || definition.GetGenericArguments().Length != args.Length)
            return open;

        var clrArgs = new Type[args.Length];
        for (var i = 0; i < args.Length; i++)
            clrArgs[i] = args[i].GetRuntimeType() ?? typeof(object);
        return clr.GetTypeDefinition(definition.MakeGenericType(clrArgs));
    }

    private static ITypeDefinition ResolveOptional(
        OptionalTypeReference opt,
        ITypeDefinitionProvider provider,
        ClrTypeDefinitionRegistry clr,
        ITypeDefinition? enclosing) {
        var innerType = Resolve(opt.InnerType, provider, enclosing);
        var innerClrType = innerType.GetRuntimeTypeOrThrow();

        if (!innerClrType.IsValueType || Nullable.GetUnderlyingType(innerClrType) != null)
            return innerType;

        var nullableType = typeof(Nullable<>).MakeGenericType(innerClrType);
        return clr.GetTypeDefinition(nullableType);
    }

    private static ITypeDefinition? ResolveByName(
        string name, ITypeDefinitionProvider provider, ClrTypeDefinitionRegistry clr) {
        if (string.Equals(name, "void", StringComparison.OrdinalIgnoreCase))
            return clr.GetTypeDefinition(typeof(void));
        return provider.GetTypeDefinition(name) ?? clr.GetTypeDefinition(name);
    }

    private static ITypeDefinition ResolveCollection(
        CollectionTypeReference col,
        ITypeDefinitionProvider provider,
        ClrTypeDefinitionRegistry clr,
        ITypeDefinition? enclosing) {
        var elementType = Resolve(col.ElementType, provider, enclosing);
        return CloseCollection(elementType, col.Kind, clr);
    }

    private static ClrTypeDefinition ResolveMap(
        MapTypeReference map,
        ITypeDefinitionProvider provider,
        ClrTypeDefinitionRegistry clr,
        ITypeDefinition? enclosing) {
        var keyType = Resolve(map.KeyType, provider, enclosing);
        var valueType = Resolve(map.ValueType, provider, enclosing);

        var dictType = typeof(Dictionary<,>).MakeGenericType(
            keyType.GetRuntimeTypeOrThrow(),
            valueType.GetRuntimeTypeOrThrow()
        );
        return clr.GetTypeDefinition(dictType);
    }

    private static ITypeDefinition ResolveUnion(
        UnionTypeReference union,
        ITypeDefinitionProvider provider,
        ClrTypeDefinitionRegistry clr,
        ITypeDefinition? enclosing) {
        if (union.Options.Count == 0) {
            return clr.GetTypeDefinition<object>();
        }

        var optionTypes = union.Options.Select(option => Resolve(option, provider, enclosing)).ToArray();
        var firstRuntimeType = optionTypes[0].GetRuntimeTypeOrThrow();

        // Preserve precision only when all options collapse to the same CLR runtime type.
        var allSameRuntimeType = optionTypes.All(type => type.GetRuntimeTypeOrThrow() == firstRuntimeType);
        return allSameRuntimeType ? optionTypes[0] : clr.GetTypeDefinition<object>();
    }
}

internal sealed class AstCollectionTypeDefinition : ITypeDefinition, IClrTypeDefinition {
    private readonly AstCollectionItemProperty _item;
    private readonly ITypeProperty? _count;
    private readonly ITypeConstructor _ctor;
    private readonly ITypeConstructor _copy;
    private readonly ITypeMethod _add;

    public AstCollectionTypeDefinition(ITypeDefinition element, CollectionKind kind) {
        ElementType = element;
        Kind = kind;
        _item = new AstCollectionItemProperty(this, element);
        _count = ClrTypeDefinitionRegistry.Shared
            .GetTypeDefinition(typeof(System.Collections.IList))
            ?.Properties.FirstOrDefault(p => string.Equals(p.Name, "Count", StringComparison.Ordinal));
        _ctor = new AstCollectionParameterlessConstructor(this);
        // new List<T>(items ?? new List<T>()) on a printed entity constructor.
        _copy = new AstCollectionCopyConstructor(this);
        // DomainResult.Value is object. Add still has to accept the entity.
        var addArg = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition<object>();
        _add = new AstCollectionAddMethod(this, addArg);
    }

    public ITypeDefinition ElementType { get; }
    public CollectionKind Kind { get; }
    public string Name => Kind == CollectionKind.Array ? $"{ElementType.Name}[]" : $"List<{ElementType.Name}>";
    public string? Namespace => null;
    public AccessModifier AccessModifier => AccessModifier.Public;
    public ITypeDefinition? BaseType => null;
    public IEnumerable<ITypeDefinition> Interfaces => [];
    public IEnumerable<IParameter> GenericParameters => [];
    public IEnumerable<ITypeMember> Members => _count is null
        ? [_item, _ctor, _copy, _add]
        : [_item, _count, _ctor, _copy, _add];
    public IEnumerable<ITypeField> Fields => [];
    public IEnumerable<ITypeProperty> Properties => _count is null ? [_item] : [_item, _count];
    public IEnumerable<ITypeMethod> Methods => [_add];
    public IEnumerable<ITypeConstructor> Constructors => [_ctor, _copy];
    public PrimitiveType? PrimitiveType => null;
    public TypeCategory TypeCategory => TypeCategory.Collection;
    public Type RuntimeType => typeof(System.Collections.IList);
}

/// <summary>Parameterless constructor for a synthetic collection type.</summary>
internal sealed class AstCollectionParameterlessConstructor(AstCollectionTypeDefinition declaring) : ITypeConstructor {
    public string Name => declaring.Name;
    public ITypeDefinition MemberTypeDefinition => declaring;
    public ITypeDefinition DeclaringTypeDefinition => declaring;
    public IEnumerable<IParameter> Parameters => [];
    public AccessModifier AccessModifier => AccessModifier.Public;
    public LifetimeModifier LifetimeModifier => LifetimeModifier.Instance;
    public Mutability Mutability => Mutability.Mutable;
}

/// <summary><c>new List&lt;T&gt;(items)</c> for a synthetic collection type.</summary>
internal sealed class AstCollectionCopyConstructor(AstCollectionTypeDefinition declaring) : ITypeConstructor {
    public string Name => declaring.Name;
    public ITypeDefinition MemberTypeDefinition => declaring;
    public ITypeDefinition DeclaringTypeDefinition => declaring;
    public IEnumerable<IParameter> Parameters => [new AstCollectionSourceParameter(declaring)];
    public AccessModifier AccessModifier => AccessModifier.Public;
    public LifetimeModifier LifetimeModifier => LifetimeModifier.Instance;
    public Mutability Mutability => Mutability.Mutable;
}

/// <summary>The source sequence of <c>new List&lt;T&gt;(items)</c>.</summary>
internal sealed class AstCollectionSourceParameter(AstCollectionTypeDefinition source) : IParameter {
    public int Position => 0;
    public string Name => "collection";
    public ITypeDefinition ParameterTypeDefinition => source;
    public bool IsOptional => false;
    public object? DefaultValue => null;
}

/// <summary><c>Add(X)</c> on a synthetic collection type.</summary>
internal sealed class AstCollectionAddMethod(AstCollectionTypeDefinition declaring, ITypeDefinition element) : ITypeMethod {
    public string Name => "Add";
    public ITypeDefinition MemberTypeDefinition => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(void))!;
    public ITypeDefinition DeclaringTypeDefinition => declaring;
    public IEnumerable<IParameter> Parameters => [new AstCollectionElementParameter(element)];
    public AccessModifier AccessModifier => AccessModifier.Public;
    public LifetimeModifier LifetimeModifier => LifetimeModifier.Instance;
    public bool IsStatic => false;
    public Mutability Mutability => Mutability.Mutable;
}

/// <summary>The element parameter of <c>Add(X)</c>.</summary>
internal sealed class AstCollectionElementParameter(ITypeDefinition element) : IParameter {
    public int Position => 0;
    public string Name => "item";
    public ITypeDefinition ParameterTypeDefinition => element;
    public bool IsOptional => false;
    public object? DefaultValue => null;
}

internal sealed class AstGenericParameterTypeDefinition : ITypeDefinition, IClrTypeDefinition {
    public AstGenericParameterTypeDefinition(IParameter parameter, ITypeDefinition declaringType) {
        Name = parameter.Name;
        DeclaringType = declaringType;
    }

    public string Name { get; }
    public ITypeDefinition DeclaringType { get; }
    public string? Namespace => null;
    public AccessModifier AccessModifier => AccessModifier.Public;
    public ITypeDefinition? BaseType => null;
    public IEnumerable<ITypeDefinition> Interfaces => [];
    public IEnumerable<IParameter> GenericParameters => [];
    public IEnumerable<ITypeMember> Members => [];
    public IEnumerable<ITypeField> Fields => [];
    public IEnumerable<ITypeProperty> Properties => [];
    public IEnumerable<ITypeMethod> Methods => [];
    public IEnumerable<ITypeConstructor> Constructors => [];
    public PrimitiveType? PrimitiveType => null;
    public TypeCategory TypeCategory => TypeCategory.None;
    public Type RuntimeType => typeof(object);
}

internal sealed class AstCollectionItemProperty : ITypeProperty {
    public AstCollectionItemProperty(ITypeDefinition declaring, ITypeDefinition element) {
        DeclaringTypeDefinition = declaring;
        MemberTypeDefinition = element;
        Parameters = [
            new AstIndexParameter(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition<int>())
        ];
    }

    public string Name => "Item";
    public ITypeDefinition MemberTypeDefinition { get; }
    public ITypeDefinition DeclaringTypeDefinition { get; }
    public IEnumerable<IParameter> Parameters { get; }
    public AccessModifier AccessModifier => AccessModifier.Public;
    public LifetimeModifier LifetimeModifier => LifetimeModifier.Instance;
    public Mutability Mutability => Mutability.Mutable;
}

internal sealed class AstIndexParameter(ITypeDefinition type) : IParameter {
    public int Position => 0;
    public string Name => "index";
    public ITypeDefinition ParameterTypeDefinition { get; } = type;
    public bool IsOptional => false;
    public object? DefaultValue => null;
}