using System.Collections.Frozen;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.DataModeling.Interpretation;

internal sealed class DataTypeDefinition : ITypeDefinition {
    private readonly DataType _dataType;
    private readonly FrozenDictionary<string, DataTypeMember> _members;
    private readonly string _name;
    private readonly ITypeDefinitionProvider? _provider;

    public DataTypeDefinition(DataType dataType, ITypeDefinitionProvider? provider = null) {
        _dataType = dataType ?? throw new ArgumentNullException(nameof(dataType));
        _name = dataType.Name;
        _provider = provider;
        _members = dataType.Properties
            .Select(p => new DataTypeMember(this, p, provider))
            .ToFrozenDictionary(m => m.Name);
    }

    public string Name => _name;
    public string? Namespace => null;
    public IEnumerable<ITypeMember> Members => _members.Values;
    public IEnumerable<IMethod> Methods => Array.Empty<IMethod>();
    public Type ReflectedType => typeof(IDictionary<string, object>);

    public ITypeMember? GetMember(string name) => _members.TryGetValue(name, out var m) ? m : null;

    public IEnumerable<ITypeMember> GetMembers(string name) {
        throw new NotImplementedException();
    }
}

internal sealed class DataTypeMember : ITypeMember {
    private readonly DataTypeDefinition _declaring;
    private readonly DataProperty _property;
    private readonly Lazy<ITypeDefinition> _memberType;

    public DataTypeMember(DataTypeDefinition declaring, DataProperty property, ITypeDefinitionProvider? provider) {
        _declaring = declaring ?? throw new ArgumentNullException(nameof(declaring));
        _property = property ?? throw new ArgumentNullException(nameof(property));
        _memberType = new Lazy<ITypeDefinition>(() => ResolveMemberType(property, provider));
        Name = property.Name;
    }

    public string Name { get; }
    public ITypeDefinition DeclaringTypeDefinition => _declaring;
    public ITypeDefinition MemberTypeDefinition => _memberType.Value;

    public IEnumerable<IParameter>? Parameters { get; }

    public Poly.Interpretation.Value GetMemberAccessor(Poly.Interpretation.Value instance, params IEnumerable<Poly.Interpretation.Value>? _) {
        return new DataModelPropertyAccessor(instance, Name, MemberTypeDefinition);
    }

    private static ITypeDefinition ResolveMemberType(DataProperty property, ITypeDefinitionProvider? provider) {
        var clr = ClrTypeDefinitionRegistry.Shared;
        
        if (property is ReferenceProperty refProp) {
            // Try to resolve from provider first (DataModel types)
            if (provider != null) {
                var typeDef = provider.GetTypeDefinition(refProp.ReferencedTypeName);
                if (typeDef != null) return typeDef;
            }
            // Fallback to object if type not found
            return clr.GetTypeDefinition<object>()!;
        }
        
        return property switch {
            StringProperty => clr.GetTypeDefinition<string>()!,
            Int32Property => clr.GetTypeDefinition<int>()!,
            Int64Property => clr.GetTypeDefinition<long>()!,
            DoubleProperty => clr.GetTypeDefinition<double>()!,
            BooleanProperty => clr.GetTypeDefinition<bool>()!,
            GuidProperty => clr.GetTypeDefinition<Guid>()!,
            DateTimeProperty => clr.GetTypeDefinition<DateTime>()!,
            DateOnlyProperty => clr.GetTypeDefinition<DateOnly>()!,
            TimeOnlyProperty => clr.GetTypeDefinition<TimeOnly>()!,
            DecimalProperty => clr.GetTypeDefinition<decimal>()!,
            ByteArrayProperty => clr.GetTypeDefinition<byte[]>()!,
            JsonProperty => clr.GetTypeDefinition<object>()!,
            EnumProperty => clr.GetTypeDefinition<string>()!, // enums treated as strings by default here
            _ => clr.GetTypeDefinition<object>()!
        };
    }
}
