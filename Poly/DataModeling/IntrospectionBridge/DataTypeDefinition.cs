using System.Collections.Frozen;

using Poly.DataModeling.TypeExpressions;
using Poly.Introspection.CommonLanguageRuntime;

using CollectionKind = Poly.DataModeling.TypeExpressions.CollectionKind;

namespace Poly.DataModeling.IntrospectionBridge;

public sealed class DataTypeDefinition : ITypeDefinition {
    private readonly DataType _dataType;
    private readonly Lazy<FrozenDictionary<string, DataTypeMember>> _members;
    private readonly string _name;
    private readonly ITypeDefinitionProvider _provider;

    public DataTypeDefinition(DataType dataType, ITypeDefinitionProvider provider)
    {
        _dataType = dataType ?? throw new ArgumentNullException(nameof(dataType));
        _name = dataType.Name;
        _provider = provider;
        _members = new(MemberDictionaryFactory);
    }

    public string Name => _name;
    public string? Namespace => null;
    public IEnumerable<ITypeMember> Members => _members.Value.Values;
    public IEnumerable<ITypeField> Fields => Enumerable.Empty<ITypeField>();
    public IEnumerable<ITypeProperty> Properties => _members.Value.Values; // Assuming all members are properties
    public IEnumerable<ITypeMethod> Methods => Enumerable.Empty<ITypeMethod>();

    public Type ReflectedType => typeof(IDictionary<string, object>); // DataTypes backed by dictionaries
    public ITypeDefinition? BaseType => null;
    public IEnumerable<ITypeDefinition> Interfaces => Enumerable.Empty<ITypeDefinition>();
    public IEnumerable<IParameter> GenericParameters => [];
    public PrimitiveTypeId? PrimitiveTypeId => null; // Data model types are not primitive
    public TypeCategory TypeCategory => ComputeTypeCategory();

    private FrozenDictionary<string, DataTypeMember> MemberDictionaryFactory()
    {
        return _dataType
            .Properties
            .Select(e => new DataTypeMember(this, e, _provider))
            .ToFrozenDictionary(m => m.Name);
    }

    private TypeCategory ComputeTypeCategory()
    {
        // DataTypes are complex reference types
        // Could compute based on properties (all numeric, all text, etc.) but for now keep simple
        return TypeCategory.None;
    }
}

internal sealed class DataTypeMember : ITypeProperty {
    private readonly DataTypeDefinition _declaring;
    private readonly DataProperty _property;
    private readonly Lazy<ITypeDefinition> _memberType;

    public DataTypeMember(DataTypeDefinition declaring, DataProperty property, ITypeDefinitionProvider provider)
    {
        _declaring = declaring ?? throw new ArgumentNullException(nameof(declaring));
        _property = property ?? throw new ArgumentNullException(nameof(property));
        _memberType = new Lazy<ITypeDefinition>(() => ResolveMemberType(property, provider));
        Name = property.Name;
    }

    public string Name { get; }
    public ITypeDefinition DeclaringType => _declaring;
    public ITypeDefinition MemberType => _memberType.Value;

    ITypeDefinition ITypeMember.MemberTypeDefinition => MemberType;
    ITypeDefinition ITypeMember.DeclaringTypeDefinition => DeclaringType;

    public IEnumerable<IParameter>? Parameters { get; }

    /// <summary>
    /// Data model properties are always instance members (not static).
    /// </summary>
    public bool IsStatic => false;

    public Node GetMemberAccessor(Node instance, params Node[]? parameters) => new DataModelPropertyAccessor(instance, Name, MemberType);

    private static ITypeDefinition ResolveMemberType(DataProperty property, ITypeDefinitionProvider provider)
    {
        var clr = ClrTypeDefinitionRegistry.Shared;

        return ResolveTypeExpression(property.Type, provider, clr);
    }

    private static ITypeDefinition ResolveTypeExpression(TypeExpression typeExpr, ITypeDefinitionProvider provider, ClrTypeDefinitionRegistry clr)
    {
        return typeExpr switch {
            ReferenceType refType => provider.GetTypeDefinition(refType.TypeName) ?? clr.GetTypeDefinition<object>(),
            OptionalType optType => ResolveOptionalType(optType, provider, clr),
            CollectionType colType => ResolveCollectionType(colType, provider, clr),
            MapType mapType => ResolveMapType(mapType, provider, clr),
            EnumType => clr.GetTypeDefinition<string>(), // Enums treated as strings for now
            PrimitiveType primType => ResolvePrimitiveType(primType.Id, clr),
            _ => clr.GetTypeDefinition<object>()
        };
    }

    private static ITypeDefinition ResolveOptionalType(OptionalType optType, ITypeDefinitionProvider provider, ClrTypeDefinitionRegistry clr)
    {
        var innerType = ResolveTypeExpression(optType.Inner, provider, clr);
        var innerClrType = innerType.ReflectedType;

        // If already a reference type or nullable, return as-is
        if (!innerClrType.IsValueType || Nullable.GetUnderlyingType(innerClrType) != null)
            return innerType;

        // Wrap value types in Nullable<T>
        var nullableType = typeof(Nullable<>).MakeGenericType(innerClrType);
        return clr.GetTypeDefinition(nullableType);
    }

    private static ITypeDefinition ResolveCollectionType(CollectionType colType, ITypeDefinitionProvider provider, ClrTypeDefinitionRegistry clr)
    {
        var elementType = ResolveTypeExpression(colType.Element, provider, clr);
        var elementClrType = elementType.ReflectedType;

        // Map to appropriate collection type based on CollectionKind
        var collectionClrType = colType.Kind switch {
            CollectionKind.Array => elementClrType.MakeArrayType(),
            CollectionKind.List => typeof(List<>).MakeGenericType(elementClrType),
            CollectionKind.Set => typeof(HashSet<>).MakeGenericType(elementClrType),
            _ => typeof(IEnumerable<>).MakeGenericType(elementClrType)
        };

        return clr.GetTypeDefinition(collectionClrType);
    }

    private static ITypeDefinition ResolveMapType(MapType mapType, ITypeDefinitionProvider provider, ClrTypeDefinitionRegistry clr)
    {
        var keyType = ResolveTypeExpression(mapType.Key, provider, clr);
        var valueType = ResolveTypeExpression(mapType.Value, provider, clr);

        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(keyType.ReflectedType, valueType.ReflectedType);
        return clr.GetTypeDefinition(dictionaryType);
    }

    private static ITypeDefinition ResolvePrimitiveType(PrimitiveTypeId id, ClrTypeDefinitionRegistry clr)
    {
        return id switch {
            PrimitiveTypeId.Boolean => clr.GetTypeDefinition<bool>(),
            PrimitiveTypeId.Int8 => clr.GetTypeDefinition<sbyte>(),
            PrimitiveTypeId.Int16 => clr.GetTypeDefinition<short>(),
            PrimitiveTypeId.Int32 => clr.GetTypeDefinition<int>(),
            PrimitiveTypeId.Int64 => clr.GetTypeDefinition<long>(),
            PrimitiveTypeId.UInt8 => clr.GetTypeDefinition<byte>(),
            PrimitiveTypeId.UInt16 => clr.GetTypeDefinition<ushort>(),
            PrimitiveTypeId.UInt32 => clr.GetTypeDefinition<uint>(),
            PrimitiveTypeId.UInt64 => clr.GetTypeDefinition<ulong>(),
            PrimitiveTypeId.Float32 => clr.GetTypeDefinition<float>(),
            PrimitiveTypeId.Float64 => clr.GetTypeDefinition<double>(),
            PrimitiveTypeId.Decimal => clr.GetTypeDefinition<decimal>(),
            PrimitiveTypeId.String => clr.GetTypeDefinition<string>(),
            PrimitiveTypeId.Char => clr.GetTypeDefinition<char>(),
            PrimitiveTypeId.DateTime => clr.GetTypeDefinition<DateTime>(),
            PrimitiveTypeId.DateOnly => clr.GetTypeDefinition<DateOnly>(),
            PrimitiveTypeId.TimeOnly => clr.GetTypeDefinition<TimeOnly>(),
            PrimitiveTypeId.TimeSpan => clr.GetTypeDefinition<TimeSpan>(),
            PrimitiveTypeId.Guid => clr.GetTypeDefinition<Guid>(),
            PrimitiveTypeId.ByteArray => clr.GetTypeDefinition<byte[]>(),
            PrimitiveTypeId.Json => clr.GetTypeDefinition<object>(),
            _ => clr.GetTypeDefinition<object>()
        };
    }
}