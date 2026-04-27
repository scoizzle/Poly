using System.Collections.Frozen;

using Poly.Introspection.CommonLanguageRuntime;
using Poly.Syntax.AbstractSyntaxTree;
using Poly.Syntax.Analysis;

namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// Analyzer that extracts ITypeDefinition instances from TypeDefinitionNode AST nodes.
/// Stores the extracted type definitions in the analysis context for use by other analyzers.
/// Also acts as an ITypeDefinitionProvider for the analyzed types.
/// </summary>
public sealed class TypeDefinitionNodeAnalyzer : INodeAnalyzer, ITypeDefinitionProvider {
    private readonly Dictionary<string, AstTypeDefinition> _types = new();
    private FrozenDictionary<string, AstTypeDefinition>? _frozen;

    public void Analyze(AnalysisContext context, Node node) {
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
    private readonly Lazy<IReadOnlyList<AstPropertyDefinition>> _properties;
    private readonly Lazy<IReadOnlyList<AstMethodDefinition>> _methods;
    private readonly Lazy<IReadOnlyList<AstFieldDefinition>> _fields;

    public AstTypeDefinition(TypeDefinitionNode node, ITypeDefinitionProvider provider) {
        _node = node;
        _provider = provider;
        _baseType = new(() => _node.BaseType is null ? null : ResolveType(_node.BaseType));
        _interfaces = new(() => _node.Interfaces?.Select(ResolveType).ToArray() ?? []);
        _genericParameters = new(() => MapParameters(_node.GenericParameters));
        _constructors = new(() => BuildConstructors());
        _properties = new(() => BuildProperties());
        _methods = new(() => BuildMethods());
        _fields = new(() => BuildFields());
    }

    public string Name => _node.Name;
    public string? Namespace => _node.Namespace;
    public string FullName => _node.FullName;

    public IEnumerable<ITypeMember> Members => [.. Constructors, .. Properties, .. Methods, .. Fields];

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

    private IReadOnlyList<AstPropertyDefinition> BuildProperties() {
        return _node.Properties?
            .Select(p => new AstPropertyDefinition(p, this, _provider))
            .ToList() ?? [];
    }

    private IReadOnlyList<AstConstructorDefinition> BuildConstructors() {
        return _node.Constructors?
            .Select(constructor => new AstConstructorDefinition(constructor, this))
            .ToList() ?? [];
    }

    private IReadOnlyList<AstMethodDefinition> BuildMethods() {
        return _node.Methods?
            .Select(m => new AstMethodDefinition(m, this, _provider))
            .ToList() ?? [];
    }

    private IReadOnlyList<AstFieldDefinition> BuildFields() {
        return _node.Fields?
            .Select(f => new AstFieldDefinition(f, this, _provider))
            .ToList() ?? [];
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
    IEnumerable<IParameter>? ITypeMember.Parameters => Parameters;
    public bool IsStatic => false;
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
internal sealed class AstPropertyDefinition : ITypeProperty {
    private readonly PropertyDefinitionNode _node;
    private readonly AstTypeDefinition _declaring;
    private readonly Lazy<ITypeDefinition> _memberType;

    public AstPropertyDefinition(PropertyDefinitionNode node, AstTypeDefinition declaring, ITypeDefinitionProvider provider) {
        _node = node;
        _declaring = declaring;
        _memberType = new(() => declaring.ResolveType(node.MemberType));
    }

    public string Name => _node.Name;
    public ITypeDefinition MemberTypeDefinition => _memberType.Value;
    public ITypeDefinition DeclaringTypeDefinition => _declaring;
    public IEnumerable<IParameter>? Parameters => _node.IndexParameters is null ? null : _declaring.MapParameters(_node.IndexParameters);
    public bool IsStatic => _node.IsStatic;
}

/// <summary>
/// ITypeMethod implementation backed by a MethodDefinitionNode AST.
/// </summary>
internal sealed class AstMethodDefinition : ITypeMethod {
    private readonly MethodDefinitionNode _node;
    private readonly AstTypeDefinition _declaring;
    private readonly Lazy<ITypeDefinition> _returnType;

    public AstMethodDefinition(MethodDefinitionNode node, AstTypeDefinition declaring, ITypeDefinitionProvider provider) {
        _node = node;
        _declaring = declaring;
        _returnType = new(() => declaring.ResolveType(node.ReturnType));
    }

    public string Name => _node.Name;
    public ITypeDefinition MemberTypeDefinition => _returnType.Value;
    public ITypeDefinition DeclaringTypeDefinition => _declaring;
    public IEnumerable<IParameter> Parameters => _declaring.MapParameters(_node.Parameters);
    public bool IsStatic => _node.IsStatic;
}

/// <summary>
/// ITypeField implementation backed by a FieldDefinitionNode AST.
/// </summary>
internal sealed class AstFieldDefinition : ITypeField {
    private readonly FieldDefinitionNode _node;
    private readonly AstTypeDefinition _declaring;
    private readonly Lazy<ITypeDefinition> _fieldType;

    public AstFieldDefinition(FieldDefinitionNode node, AstTypeDefinition declaring, ITypeDefinitionProvider provider) {
        _node = node;
        _declaring = declaring;
        _fieldType = new(() => declaring.ResolveType(node.FieldType));
    }

    public string Name => _node.Name;
    public ITypeDefinition MemberTypeDefinition => _fieldType.Value;
    public ITypeDefinition DeclaringTypeDefinition => _declaring;
    public IEnumerable<IParameter>? Parameters => null;
    public bool IsStatic => _node.IsStatic;
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
            _ => clr.GetTypeDefinition<object>()
        };
    }

    private static ITypeDefinition ResolvePrimitive(PrimitiveType id, bool isNullable, ClrTypeDefinitionRegistry clr) {
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

    private static ITypeDefinition ResolveCollection(CollectionTypeReference col, ITypeDefinitionProvider provider, ClrTypeDefinitionRegistry clr) {
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

    private static ITypeDefinition ResolveMap(MapTypeReference map, ITypeDefinitionProvider provider, ClrTypeDefinitionRegistry clr) {
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