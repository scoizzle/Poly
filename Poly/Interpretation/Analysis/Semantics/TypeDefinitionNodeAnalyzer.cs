using System.Linq.Expressions;
using System.Reflection;

using Poly.Interpretation.Vm;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// Analyzer that extracts ITypeDefinition instances from TypeDefinitionNode AST nodes.
/// Stores the extracted type definitions in the analysis context for use by other analyzers.
/// Also acts as an ITypeDefinitionProvider for the analyzed types.
/// </summary>
public sealed class TypeDefinitionNodeAnalyzer : INodeAnalyzer, ITypeDefinitionProvider {
    public const string Id = "TypeDefinitionNode";
    public string PassName => Id;
    private AstTypeRegistry? _registry;

    public void Analyze(AnalysisContext context, Node node) {
        var registry = context.GetMetadata<AstTypeRegistry>(default);
        if (registry is null) {
            registry = new AstTypeRegistry();
            context.SetMetadata(default, registry);
            context.TypeDefinitions.Add(registry);
            _registry = registry;
        }

        if (node is TypeDefinitionNode typeDef) {
            var definition = new AstTypeDefinition(typeDef, context.TypeDefinitions);
            registry.Add(definition);
            context.SetMetadata(node, new TypeDefinitionMetadata(definition));
        }

        this.AnalyzeChildren(context, node);
    }

    public ITypeDefinition? GetTypeDefinition(string typeName) =>
        _registry?.GetTypeDefinition(typeName);

    public ITypeDefinition? GetTypeDefinition(Type type) =>
        ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);

    public IEnumerable<ITypeDefinition> GetTypeDefinitions() =>
        _registry?.GetTypeDefinitions() ?? [];
}

/// <summary>
/// Per-analysis AST type table. Lives on <see cref="AnalysisContext"/> so the
/// shared <see cref="Interpreter.Analyzer"/> instance cannot leak or clear
/// types across concurrent analyses.
/// </summary>
internal sealed class AstTypeRegistry : ITypeDefinitionProvider, IAnalysisMetadata {
    private readonly Dictionary<string, AstTypeDefinition> _types = new(StringComparer.Ordinal);

    public void Add(AstTypeDefinition definition) {
        _types[definition.FullName] = definition;
    }

    public IEnumerable<ITypeDefinition> GetTypeDefinitions() => _types.Values;

    public ITypeDefinition? GetTypeDefinition(string typeName) {
        if (_types.TryGetValue(typeName, out var def))
            return def;

        AstTypeDefinition? match = null;
        foreach (var type in _types.Values) {
            if (!string.Equals(type.Name, typeName, StringComparison.Ordinal))
                continue;
            if (match is not null)
                return null;
            match = type;
        }
        return match;
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
    /// Returns the property's <c>DefaultValue</c> (or <see cref="System.Reflection.Missing.Value"/>)
    /// when the key is not present.
    /// </summary>
    public Expression? EmitRead(Expression? instance) {
        if (instance is null) return null;
        var typed = Expression.Convert(instance, typeof(IDictionary<string, object?>));
        var rawValue = Expression.Call(typed, DictionaryBackedValue.DictGetItem, Expression.Constant(Name));
        object? def = _node.DefaultValue is Constant c ? c.Value : System.Reflection.Missing.Value;
        var fallback = Expression.Convert(Expression.Constant(def), typeof(object));
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
        if (instance is null) return null;
        var typed = Expression.Convert(instance, typeof(IDictionary<string, object?>));
        var rawValue = Expression.Call(typed, DictionaryBackedValue.DictGetItem, Expression.Constant(Name));
        object? def = _node.DefaultValue is Constant c ? c.Value : System.Reflection.Missing.Value;
        var fallback = Expression.Convert(Expression.Constant(def), typeof(object));
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
    /// Emits an expression that coerces a dictionary value (typed <c>object?</c>)
    /// to the target member's declared type. For primitive types this uses
    /// <see cref="Convert"/> methods (e.g. <c>Convert.ToInt64</c>); for
    /// reference types the value passes through unchanged.
    /// </summary>
    internal static Expression CoerceRead(Expression dictValue, ITypeDefinition targetType) {
        var primitive = targetType.PrimitiveType;
        if (primitive is null) return dictValue;

        var convertMethod = primitive.Value switch {
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

        if (convertMethod is not null) {
            // Fail loud on fundamentally wrong-typed raw values instead of silently
            // coercing (Convert.ToInt64(true) → 1, Convert.ToString(null) → "", etc.):
            // a property bag holding a bool for a Number prop (or a non-numeric string)
            // must surface as an error, not a silently-mangled value.
            var guarded = Expression.Call(
                GuardCompatibleInfo,
                Expression.Convert(dictValue, typeof(object)),
                Expression.Constant(primitive.Value));
            return Expression.Convert(
                Expression.Call(null, convertMethod, guarded),
                typeof(object));
        }

        return dictValue;
    }

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
            for (var i = 0; i < args.Count; i++) {
                var resolved = TryResolve(args[i], provider, enclosing);
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

        var open = LookupNamed(named, provider, clr, enclosing, allowTypeParameter: false);
        if (open is null)
            return null;
        return CloseGeneric(open, args, clr);
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

    public AstCollectionTypeDefinition(ITypeDefinition element, CollectionKind kind) {
        ElementType = element;
        Kind = kind;
        _item = new AstCollectionItemProperty(this, element);
    }

    public ITypeDefinition ElementType { get; }
    public CollectionKind Kind { get; }
    public string Name => Kind == CollectionKind.Array ? $"{ElementType.Name}[]" : $"List<{ElementType.Name}>";
    public string? Namespace => null;
    public AccessModifier AccessModifier => AccessModifier.Public;
    public ITypeDefinition? BaseType => null;
    public IEnumerable<ITypeDefinition> Interfaces => [];
    public IEnumerable<IParameter> GenericParameters => [];
    public IEnumerable<ITypeMember> Members => [_item];
    public IEnumerable<ITypeField> Fields => [];
    public IEnumerable<ITypeProperty> Properties => [_item];
    public IEnumerable<ITypeMethod> Methods => [];
    public IEnumerable<ITypeConstructor> Constructors => [];
    public PrimitiveType? PrimitiveType => null;
    public TypeCategory TypeCategory => TypeCategory.Collection;
    public Type RuntimeType => typeof(System.Collections.IList);
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