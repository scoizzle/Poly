using System.Collections.Frozen;

using Poly.Interpretation.Analysis;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.AbstractSyntaxTree.TypeDefinitions;

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
internal sealed class AstTypeDefinition : ITypeDefinition {
    private readonly TypeDefinitionNode _node;
    private readonly ITypeDefinitionProvider _provider;
    private readonly Lazy<IReadOnlyList<AstPropertyDefinition>> _properties;
    private readonly Lazy<IReadOnlyList<AstMethodDefinition>> _methods;
    private readonly Lazy<IReadOnlyList<AstFieldDefinition>> _fields;
    private static readonly IReadOnlyList<ITypeConstructor> EmptyConstructors = [];

    public AstTypeDefinition(TypeDefinitionNode node, ITypeDefinitionProvider provider) {
        _node = node;
        _provider = provider;
        _properties = new(() => BuildProperties());
        _methods = new(() => BuildMethods());
        _fields = new(() => BuildFields());
    }

    public string Name => _node.Name;
    public string? Namespace => _node.Namespace;
    public string FullName => _node.FullName;

    public IEnumerable<ITypeMember> Members =>
        Properties.Cast<ITypeMember>()
            .Concat(Methods)
            .Concat(Fields);

    public IEnumerable<ITypeField> Fields => _fields.Value;
    public IEnumerable<ITypeProperty> Properties => _properties.Value;
    public IEnumerable<ITypeMethod> Methods => _methods.Value;
    public IEnumerable<ITypeConstructor> Constructors => EmptyConstructors;

    // AST-based types are dictionary-backed at runtime
    public Type ReflectedType => typeof(IDictionary<string, object>);

    public ITypeDefinition? BaseType => null; // TODO: Resolve from _node.BaseType
    public IEnumerable<ITypeDefinition> Interfaces => []; // TODO: Resolve from _node.Interfaces
    public IEnumerable<IParameter> GenericParameters => []; // TODO: Map from _node.GenericParameters

    public PrimitiveType? PrimitiveType => _node.PrimitiveTypeId;
    public TypeCategory TypeCategory => _node.TypeCategory;

    private IReadOnlyList<AstPropertyDefinition> BuildProperties() {
        return _node.Properties?
            .Select(p => new AstPropertyDefinition(p, this, _provider))
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

    internal ITypeDefinition ResolveType(Node typeNode) => TypeResolver.Resolve(typeNode, _provider);
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
        _memberType = new(() => declaring.ResolveType(node.PropertyType));
    }

    public string Name => _node.Name;
    public ITypeDefinition MemberTypeDefinition => _memberType.Value;
    public ITypeDefinition DeclaringTypeDefinition => _declaring;
    public IEnumerable<IParameter>? Parameters => null; // TODO: Map IndexParameters
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
    public IEnumerable<IParameter> Parameters => []; // TODO: Map _node.Parameters
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
internal static class TypeResolver {
    public static ITypeDefinition Resolve(Node typeNode, ITypeDefinitionProvider provider) {
        var clr = ClrTypeDefinitionRegistry.Shared;

        return typeNode switch {
            PrimitiveTypeReference prim => ResolvePrimitive(prim.PrimitiveId, prim.IsNullable, clr),
            NamedTypeReference named => provider.GetTypeDefinition(named.FullName) ?? clr.GetTypeDefinition<object>(),
            OptionalTypeReference opt => ResolveOptional(opt, provider, clr),
            CollectionTypeReference col => ResolveCollection(col, provider, clr),
            MapTypeReference map => ResolveMap(map, provider, clr),
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

        if (isNullable && baseType.Type.IsValueType) {
            var nullableType = typeof(Nullable<>).MakeGenericType(baseType.Type);
            return clr.GetTypeDefinition(nullableType);
        }

        return baseType;
    }

    private static ITypeDefinition ResolveOptional(OptionalTypeReference opt, ITypeDefinitionProvider provider, ClrTypeDefinitionRegistry clr) {
        var innerType = Resolve(opt.InnerType, provider);
        var innerClrType = innerType.ReflectedType;

        if (!innerClrType.IsValueType || Nullable.GetUnderlyingType(innerClrType) != null)
            return innerType;

        var nullableType = typeof(Nullable<>).MakeGenericType(innerClrType);
        return clr.GetTypeDefinition(nullableType);
    }

    private static ITypeDefinition ResolveCollection(CollectionTypeReference col, ITypeDefinitionProvider provider, ClrTypeDefinitionRegistry clr) {
        var elementType = Resolve(col.ElementType, provider);
        var elementClrType = elementType.ReflectedType;

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

        var dictType = typeof(Dictionary<,>).MakeGenericType(keyType.ReflectedType, valueType.ReflectedType);
        return clr.GetTypeDefinition(dictType);
    }
}