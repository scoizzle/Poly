using System.Collections.Frozen;

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
    private FrozenDictionary<string, AstTypeDefinition>? _frozen;

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<TypeDefinitionNodeAnalyzer>(node)) {
            return;
        }

        if (node is TypeDefinitionNode typeDef) {
            var definition = new AstTypeDefinition(typeDef, this);
            _types[typeDef.FullName] = definition;

            // Store the type definition in context metadata
            context.SetMetadata(node, new TypeDefinitionMetadata(definition));
        }

        // Analyze children (properties, methods, etc.)
        this.AnalyzeChildren(context, node);
    }

    /// <summary>
    /// Freezes the type definitions for thread-safe read access.
    /// Call this after all TypeDefinitionNodes have been analyzed.
    /// </summary>
    public void Freeze() {
        _frozen = _types.ToFrozenDictionary();
    }

    public ITypeDefinition? GetTypeDefinition(string typeName) {
        var dict = _frozen ?? (IReadOnlyDictionary<string, AstTypeDefinition>)_types;
        return dict.TryGetValue(typeName, out var def) ? def : null;
    }

    public ITypeDefinition? GetTypeDefinition(Type type) {
        // AST-based types don't map to CLR types directly
        // Fall back to CLR registry for runtime types
        return ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);
    }

    public IEnumerable<ITypeDefinition> GetTypeDefinitions() {
        var dict = _frozen ?? (IReadOnlyDictionary<string, AstTypeDefinition>)_types;
        return dict.Values;
    }
}

/// <summary>
/// Metadata indicating the ITypeDefinition extracted from a TypeDefinitionNode.
/// </summary>
public sealed record TypeDefinitionMetadata(ITypeDefinition TypeDefinition) : IAnalysisMetadata;

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
/// </summary>
internal sealed class AstPropertyDefinition(PropertyDefinitionNode node, AstTypeDefinition declaring) : ITypeProperty {
    private readonly PropertyDefinitionNode _node = node;
    private readonly AstTypeDefinition _declaring = declaring;
    private readonly Lazy<ITypeDefinition> _memberType = new(() => declaring.ResolveType(node.MemberType));

    public string Name => _node.Name;
    public ITypeDefinition MemberTypeDefinition => _memberType.Value;
    public ITypeDefinition DeclaringTypeDefinition => _declaring;
    public IEnumerable<IParameter> Parameters => _node.IndexParameters is null ? [] : _declaring.MapParameters(_node.IndexParameters);

    public MemberReadDelegate? Read => null;
    public MemberWriteDelegate? Write => null;
    public MemberWriteDelegate? Initialize => null;

    public AccessModifier AccessModifier => _node.AccessModifier;
    public LifetimeModifier LifetimeModifier => _node.IsStatic ? LifetimeModifier.Static : LifetimeModifier.Instance;
    public bool IsStatic => _node.IsStatic;

    public Mutability Mutability => _node.Mutability;
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

    public MemberReadDelegate? Read => null;
    public MemberWriteDelegate? Write => null;
    public MemberWriteDelegate? Initialize => null;

    public AccessModifier AccessModifier => _node.AccessModifier;
    public LifetimeModifier LifetimeModifier => _node.IsStatic ? LifetimeModifier.Static : LifetimeModifier.Instance;
    public bool IsStatic => _node.IsStatic;

    public Mutability Mutability => _node.Mutability;
}



/// <summary>
/// Utility class to resolve AST type reference nodes to ITypeDefinition.
/// </summary>
internal static class AstTypeReferenceResolver {
    public static ITypeDefinition Resolve(Node typeNode, ITypeDefinitionProvider provider) {
        var clr = ClrTypeDefinitionRegistry.Shared;

        return typeNode switch {
            PrimitiveTypeReference prim => ResolvePrimitive(prim.PrimitiveId, prim.IsNullable, clr),
            NamedTypeReference named => provider.GetTypeDefinition(named.FullName) ?? clr.GetTypeDefinition<object>(),
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