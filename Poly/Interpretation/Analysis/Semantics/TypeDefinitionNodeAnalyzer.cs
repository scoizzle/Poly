using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.Introspection;
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
    private readonly Dictionary<string, AstTypeDefinition> _types = new();
    private TypeDefinitionProviderCollection? _lastRegisteredCollection;

    public void Analyze(AnalysisContext context, Node node) {
        if (node is TypeDefinitionNode typeDef) {
            if (context.TypeDefinitions != _lastRegisteredCollection) {
                context.TypeDefinitions.Add(this);
                _lastRegisteredCollection = context.TypeDefinitions;
            }
            var definition = new AstTypeDefinition(typeDef, this);
            _types[typeDef.FullName] = definition;

            // Store the type definition in context metadata for resolution
            // by other passes (e.g. ThisReferenceContextPass) and for
            // GetResolvedType queries.
            context.SetMetadata(node, new TypeDefinitionMetadata(definition));
        }

        // Analyze children (properties, methods, etc.)
        this.AnalyzeChildren(context, node);
    }

    public ITypeDefinition? GetTypeDefinition(string typeName) {
        return _types.TryGetValue(typeName, out var def) ? def : null;
    }

    public ITypeDefinition? GetTypeDefinition(Type type) {
        // AST-based types don't map to CLR types directly
        // Fall back to CLR registry for runtime types
        return ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);
    }

    public IEnumerable<ITypeDefinition> GetTypeDefinitions() {
        return _types.Values;
    }
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

    internal ITypeDefinition ResolveType(Node typeNode) => AstTypeReferenceResolver.Resolve(typeNode, _provider);

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
    public IEnumerable<IParameter> Parameters => _node.IndexParameters is null ? [] : _declaring.MapParameters(_node.IndexParameters);

    public AccessModifier AccessModifier => _node.AccessModifier;
    public LifetimeModifier LifetimeModifier => _node.IsStatic ? LifetimeModifier.Static : LifetimeModifier.Instance;
    public bool IsStatic => _node.IsStatic;

    public Mutability Mutability => _node.Mutability;

    /// <summary>
    /// Emits an expression that reads this property from a <c>Dictionary&lt;string, object?&gt;</c>
    /// using the indexer. Coerces the stored value to the property's target type.
    /// Returns the property's <c>DefaultValue</c> (or <see cref="System.Reflection.Missing.Value"/>)
    /// when the key is not present.
    /// </summary>
    public Expression? EmitRead(Expression? instance) {
        if (instance is null) return null;
        var dictType = typeof(Dictionary<string, object?>);
        var contains = dictType.GetMethod("ContainsKey", [typeof(string)])!;
        var getItem = dictType.GetMethod("get_Item", [typeof(string)])!;
        var typed = Expression.Convert(instance, dictType);
        var rawValue = Expression.Call(typed, getItem, Expression.Constant(Name));
        object? def = _node.DefaultValue is Constant c ? c.Value : System.Reflection.Missing.Value;
        var fallback = Expression.Convert(Expression.Constant(def), typeof(object));
        var value = DictionaryBackedValue.CoerceRead(rawValue, MemberTypeDefinition);
        return Expression.Condition(
            Expression.Call(typed, contains, Expression.Constant(Name)),
            value,
            fallback);
    }

    public Expression? EmitWrite(Expression? instance, Expression value) {
        if (instance is null) return null;
        var dictType = typeof(Dictionary<string, object?>);
        var typed = Expression.Convert(instance, dictType);
        var setItem = dictType.GetMethod("set_Item", [typeof(string), typeof(object)])!;
        return Expression.Block(
            Expression.Call(typed, setItem, Expression.Constant(Name), value),
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
        var dictType = typeof(Dictionary<string, object?>);
        var contains = dictType.GetMethod("ContainsKey", [typeof(string)])!;
        var getItem = dictType.GetMethod("get_Item", [typeof(string)])!;
        var typed = Expression.Convert(instance, dictType);
        var rawValue = Expression.Call(typed, getItem, Expression.Constant(Name));
        object? def = _node.DefaultValue is Constant c ? c.Value : System.Reflection.Missing.Value;
        var fallback = Expression.Convert(Expression.Constant(def), typeof(object));
        var value = DictionaryBackedValue.CoerceRead(rawValue, MemberTypeDefinition);
        return Expression.Condition(
            Expression.Call(typed, contains, Expression.Constant(Name)),
            value,
            fallback);
    }

    public Expression? EmitWrite(Expression? instance, Expression value) {
        if (instance is null) return null;
        var dictType = typeof(Dictionary<string, object?>);
        var typed = Expression.Convert(instance, dictType);
        var setItem = dictType.GetMethod("set_Item", [typeof(string), typeof(object)])!;
        return Expression.Block(
            Expression.Call(typed, setItem, Expression.Constant(Name), value),
            instance);
    }
}

/// <summary>
/// Helper for reading/writing values from dictionary-backed AST type instances.
/// Provides type coercion so stored values (e.g. <c>int</c>) are correctly
/// converted to the declared member type (e.g. <c>long</c> for Number).
/// </summary>
internal static class DictionaryBackedValue {
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
            PrimitiveType.Int64 => typeof(Convert).GetMethod(nameof(Convert.ToInt64), [typeof(object)]),
            PrimitiveType.Int32 => typeof(Convert).GetMethod(nameof(Convert.ToInt32), [typeof(object)]),
            PrimitiveType.Int16 => typeof(Convert).GetMethod(nameof(Convert.ToInt16), [typeof(object)]),
            PrimitiveType.Int8 => typeof(Convert).GetMethod("ToSByte", [typeof(object)]),
            PrimitiveType.Float64 => typeof(Convert).GetMethod(nameof(Convert.ToDouble), [typeof(object)]),
            PrimitiveType.Float32 => typeof(Convert).GetMethod(nameof(Convert.ToSingle), [typeof(object)]),
            PrimitiveType.Decimal => typeof(Convert).GetMethod(nameof(Convert.ToDecimal), [typeof(object)]),
            PrimitiveType.Boolean => typeof(Convert).GetMethod(nameof(Convert.ToBoolean), [typeof(object)]),
            PrimitiveType.String => typeof(Convert).GetMethod(nameof(Convert.ToString), [typeof(object)]),
            PrimitiveType.Char => typeof(Convert).GetMethod(nameof(Convert.ToChar), [typeof(object)]),
            _ => null
        };

        if (convertMethod is not null)
            return Expression.Convert(Expression.Call(null, convertMethod, dictValue), typeof(object));

        return dictValue;
    }
}


/// <summary>
/// Utility class to resolve AST type reference nodes to ITypeDefinition.
/// </summary>
internal static class AstTypeReferenceResolver {
    public static ITypeDefinition Resolve(Node typeNode, ITypeDefinitionProvider provider) {
        var clr = ClrTypeDefinitionRegistry.Shared;

        return typeNode switch {
            PrimitiveTypeReference prim => ResolvePrimitive(prim.PrimitiveId, prim.IsNullable, clr),
            NamedTypeReference named => provider.GetDeferredTypeDefinitionResolver(named.FullName).Value,
            OptionalTypeReference opt => ResolveOptional(opt, provider, clr),
            CollectionTypeReference col => ResolveCollection(col, provider, clr),
            MapTypeReference map => ResolveMap(map, provider, clr),
            UnionTypeReference union => ResolveUnion(union, provider, clr),
            TypeDefinitionReference tdr => tdr.TypeDefinition,
            ClrTypeReference clrRef => provider.GetTypeDefinition(clrRef.RuntimeType) ?? clr.GetTypeDefinition<object>(),
            _ => clr.GetTypeDefinition<object>()
        };
    }

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

    private static ITypeDefinition ResolveOptional(OptionalTypeReference opt, ITypeDefinitionProvider provider, ClrTypeDefinitionRegistry clr) {
        var innerType = Resolve(opt.InnerType, provider);
        var innerClrType = innerType.GetRuntimeTypeOrThrow();

        if (!innerClrType.IsValueType || Nullable.GetUnderlyingType(innerClrType) != null)
            return innerType;

        var nullableType = typeof(Nullable<>).MakeGenericType(innerClrType);
        return clr.GetTypeDefinition(nullableType);
    }

    private static ClrTypeDefinition ResolveCollection(CollectionTypeReference col, ITypeDefinitionProvider provider, ClrTypeDefinitionRegistry clr) {
        var elementType = Resolve(col.ElementType, provider);
        var elementClrType = elementType.GetRuntimeTypeOrThrow();

        var collectionClrType = col.Kind switch {
            CollectionKind.Array => elementClrType.MakeArrayType(),
            CollectionKind.List => typeof(List<>).MakeGenericType(elementClrType),
            CollectionKind.Set => typeof(HashSet<>).MakeGenericType(elementClrType),
            _ => typeof(IEnumerable<>).MakeGenericType(elementClrType)
        };

        return clr.GetTypeDefinition(collectionClrType);
    }

    private static ClrTypeDefinition ResolveMap(MapTypeReference map, ITypeDefinitionProvider provider, ClrTypeDefinitionRegistry clr) {
        var keyType = Resolve(map.KeyType, provider);
        var valueType = Resolve(map.ValueType, provider);

        var dictType = typeof(Dictionary<,>).MakeGenericType(
            keyType.GetRuntimeTypeOrThrow(),
            valueType.GetRuntimeTypeOrThrow()
        );
        return clr.GetTypeDefinition(dictType);
    }

    private static ITypeDefinition ResolveUnion(UnionTypeReference union, ITypeDefinitionProvider provider, ClrTypeDefinitionRegistry clr) {
        if (union.Options.Count == 0) {
            return clr.GetTypeDefinition<object>();
        }

        var optionTypes = union.Options.Select(option => Resolve(option, provider)).ToArray();
        var firstRuntimeType = optionTypes[0].GetRuntimeTypeOrThrow();

        // Preserve precision only when all options collapse to the same CLR runtime type.
        var allSameRuntimeType = optionTypes.All(type => type.GetRuntimeTypeOrThrow() == firstRuntimeType);
        return allSameRuntimeType ? optionTypes[0] : clr.GetTypeDefinition<object>();
    }
}