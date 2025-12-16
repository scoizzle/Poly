using System.Collections.Frozen;
using System.Reflection;

namespace Poly.Introspection.CommonLanguageRuntime;

public sealed class ClrTypeDefinition : ITypeDefinition {
    private readonly Type _type;
    private readonly FrozenSet<ClrTypeField> _fields;
    private readonly FrozenSet<ClrTypeProperty> _properties;
    private readonly FrozenSet<ClrMethod> _methods;
    private readonly FrozenSet<ClrTypeMember> _allMembers;
    private readonly FrozenDictionary<string, FrozenSet<ClrTypeMember>> _membersByName;
    private readonly ClrTypeDefinitionRegistry _provider;
    private ITypeDefinition? _baseType;
    private IEnumerable<ITypeDefinition>? _interfaces;

    public ClrTypeDefinition(Type type, ClrTypeDefinitionRegistry provider) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(provider);

        _type = type;
        _provider = provider;
        _fields = BuildFieldCollection(type, this, provider);
        _properties = BuildPropertyCollection(type, this, provider);
        _methods = BuildMethodCollection(type, this, provider);

        var combined = Enumerable.Empty<ClrTypeMember>()
            .Concat(_fields)
            .Concat(_properties)
            .Concat(_methods)
            .ToFrozenSet();
        _allMembers = combined;
        _membersByName = combined
            .GroupBy(m => m.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToFrozenSet());
    }

    public string Name => _type.Name;
    public string? Namespace => _type.Namespace;
    public string FullName => _type.FullName ?? _type.Name;
    public Type ClrType => _type;

    public IEnumerable<ClrTypeField> Fields => _fields;
    public IEnumerable<ClrTypeProperty> Properties => _properties;
    public IEnumerable<ClrMethod> Methods => _methods;
    public IEnumerable<ClrTypeMember> Members => _allMembers;
    public IEnumerable<ClrTypeMember> GetMembers(string name)
        => _membersByName.TryGetValue(name, out var members) ? members : Enumerable.Empty<ClrTypeMember>();

    public ITypeDefinition? BaseType {
        get {
            return _baseType ??= _type.BaseType != null ? _provider.GetTypeDefinition(_type.BaseType) : null;
        }
    }

    public IEnumerable<ITypeDefinition> Interfaces {
        get {
            return _interfaces ??= _type
                .GetInterfaces()
                .Select(i => _provider.GetTypeDefinition(i))
                .ToList();
        }
    }

    IEnumerable<ITypeMember> ITypeDefinition.Members => Members.Cast<ITypeMember>();
    IEnumerable<ITypeMember> ITypeDefinition.GetMembers(string name) => GetMembers(name).Cast<ITypeMember>();
    Type ITypeDefinition.ReflectedType => _type;

    public override string ToString() => FullName;

    private static readonly BindingFlags MemberSearchCriteria = BindingFlags.Public | BindingFlags.NonPublic| BindingFlags.Instance | BindingFlags.Static;

    private static FrozenSet<ClrTypeField> BuildFieldCollection(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider) {
        var fields = type
            .GetFields(MemberSearchCriteria)
            .Select(ConstructMemberField)
            .ToFrozenSet();

        return fields;

        ClrTypeField ConstructMemberField(FieldInfo fi) {
            ArgumentNullException.ThrowIfNull(fi);
            ArgumentNullException.ThrowIfNull(fi.FieldType);
            ArgumentException.ThrowIfNullOrWhiteSpace(fi.Name);

            Lazy<ClrTypeDefinition> type = provider.GetDeferredTypeDefinitionResolver(fi.FieldType);
            return new ClrTypeField(type, declaringType, fi);
        }
    }


    private static FrozenSet<ClrTypeProperty> BuildPropertyCollection(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider) {
        var properties = type.GetProperties(MemberSearchCriteria)
            .Select(ConstructMemberProperty)
            .ToFrozenSet();

        return properties;

        ClrTypeProperty ConstructMemberProperty(PropertyInfo pi) {
            ArgumentNullException.ThrowIfNull(pi);
            ArgumentNullException.ThrowIfNull(pi.PropertyType);
            ArgumentException.ThrowIfNullOrWhiteSpace(pi.Name);

            Lazy<ClrTypeDefinition> type = provider.GetDeferredTypeDefinitionResolver(pi.PropertyType);
            IEnumerable<MethodInfo> accessors = pi.GetAccessors(nonPublic: true);
            ParameterInfo[] indexParams = pi.GetIndexParameters();
            IEnumerable<ClrParameter>? parameters = indexParams.Length > 0
                ? indexParams
                    .OrderBy(pi => pi.Position)
                    .Select(pi => ConstructParameter(provider, pi))
                    .ToArray()
                : null;

            return new ClrTypeProperty(type, declaringType, parameters, pi);
        }
    }


    private static FrozenSet<ClrMethod> BuildMethodCollection(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider) {
        var methods = type
            .GetMethods(MemberSearchCriteria)
            .Where(mi => !mi.IsSpecialName) // Exclude property accessors and other special methods
            .Select(ConstructMethod)
            .ToFrozenSet();

        return methods;

        ClrMethod ConstructMethod(MethodInfo mi) {
            ArgumentNullException.ThrowIfNull(mi);

            Lazy<ClrTypeDefinition> returnType = provider.GetDeferredTypeDefinitionResolver(mi.ReturnType);
            IEnumerable<ClrParameter> parameters = mi.GetParameters().Select(pi => ConstructParameter(provider, pi)).ToArray();
            return new ClrMethod(returnType, declaringType, parameters, mi);
        }
    }

    static ClrParameter ConstructParameter(ClrTypeDefinitionRegistry provider, ParameterInfo pi) {
        ArgumentNullException.ThrowIfNull(pi);
        
        // Array indexers and some built-in methods may have null parameter names
        string parameterName = pi.Name ?? $"param{pi.Position}";

        Lazy<ClrTypeDefinition> type = provider.GetDeferredTypeDefinitionResolver(pi.ParameterType);
        return new ClrParameter(parameterName, type, pi.Position, pi.IsOptional, pi.DefaultValue);
    }
}