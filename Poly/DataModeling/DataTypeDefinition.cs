using System.Collections.Frozen;
using Poly.Introspection;

namespace Poly.DataModeling;

internal sealed class DataTypeDefinition : ITypeDefinition {
    private readonly DataType _dataType;
    private readonly Lazy<FrozenDictionary<string, DataTypeProperty>> _properties;
    private readonly ITypeDefinitionProvider _provider;

    public DataTypeDefinition(DataType dataType, ITypeDefinitionProvider provider)
    {
        _dataType = dataType ?? throw new ArgumentNullException(nameof(dataType));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _properties = new(PropertyDictionaryFactory);
    }

    public string Name => _dataType.Name;
    public string? Namespace => null;
    public string FullName => Name;
    public Type? ClrType => null;
    public Type ReflectedType => typeof(IDictionary<string, object?>);
    
    public IEnumerable<ITypeMember> Members => Properties;
    public IEnumerable<ITypeField> Fields => Enumerable.Empty<ITypeField>();
    public IEnumerable<ITypeProperty> Properties => _properties.Value.Values;
    public IEnumerable<ITypeMethod> Methods => Enumerable.Empty<ITypeMethod>();

    public bool TryGetMethod(string name, IEnumerable<Type> parameterTypes, out ITypeMethod? method)
    {
        method = null;
        return false;
    }

    public ITypeDefinition? BaseType => null;
    public IEnumerable<ITypeDefinition> Interfaces => Enumerable.Empty<ITypeDefinition>();
    public IEnumerable<IParameter> GenericParameters => [];

    public bool IsArray => false;
    public bool IsNullable => true;
    public bool IsNumeric => false;
    public ITypeDefinition? ElementType => null;
    public ITypeDefinition? UnderlyingType => null;

    private FrozenDictionary<string, DataTypeProperty> PropertyDictionaryFactory()
    {
        return _dataType
            .Properties
            .Select(e => new DataTypeProperty(this, e, _provider))
            .ToFrozenDictionary(p => p.Name);
    }
}

internal sealed class DataTypeProperty : ITypeProperty {
    private readonly DataTypeDefinition _declaring;
    private readonly DataProperty _property;
    private readonly Lazy<ITypeDefinition> _memberType;

    public DataTypeProperty(DataTypeDefinition declaring, DataProperty property, ITypeDefinitionProvider provider)
    {
        _declaring = declaring ?? throw new ArgumentNullException(nameof(declaring));
        _property = property ?? throw new ArgumentNullException(nameof(property));
        _memberType = new Lazy<ITypeDefinition>(() => ResolveMemberType(property, provider));
    }

    public string Name => _property.Name;
    public ITypeDefinition DeclaringTypeDefinition => _declaring;
    public ITypeDefinition MemberTypeDefinition => _memberType.Value;
    public bool CanRead => true;
    public bool CanWrite => true;
    public IEnumerable<IParameter>? Parameters => null;
    public bool IsStatic => false;

    private static ITypeDefinition ResolveMemberType(DataProperty property, ITypeDefinitionProvider provider)
    {
        if (property is ReferenceProperty refProp) {
            return provider.GetTypeDefinition(refProp.ReferencedTypeName)
                ?? throw new InvalidOperationException($"Referenced type '{refProp.ReferencedTypeName}' not found.");
        }

        return property switch {
            StringProperty => provider.GetTypeDefinition(typeof(string).FullName!)!,
            Int32Property => provider.GetTypeDefinition(typeof(int).FullName!)!,
            Int64Property => provider.GetTypeDefinition(typeof(long).FullName!)!,
            DoubleProperty => provider.GetTypeDefinition(typeof(double).FullName!)!,
            BooleanProperty => provider.GetTypeDefinition(typeof(bool).FullName!)!,
            GuidProperty => provider.GetTypeDefinition(typeof(Guid).FullName!)!,
            DateTimeProperty => provider.GetTypeDefinition(typeof(DateTime).FullName!)!,
            DateOnlyProperty => provider.GetTypeDefinition(typeof(DateOnly).FullName!)!,
            TimeOnlyProperty => provider.GetTypeDefinition(typeof(TimeOnly).FullName!)!,
            DecimalProperty => provider.GetTypeDefinition(typeof(decimal).FullName!)!,
            ByteArrayProperty => provider.GetTypeDefinition(typeof(byte[]).FullName!)!,
            JsonProperty => provider.GetTypeDefinition(typeof(object).FullName!)!,
            EnumProperty enumProp => provider.GetTypeDefinition(typeof(string).FullName!)!,
            _ => provider.GetTypeDefinition(typeof(object).FullName!)!
        };
    }
}
