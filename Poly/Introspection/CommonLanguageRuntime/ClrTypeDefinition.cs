using System.Collections.Frozen;
using System.Reflection;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// CLR-backed implementation of <see cref="ITypeDefinition"/> that uses reflection to surface
/// fields, properties, and methods, with immutable frozen collections for fast lookups.
/// Thread-safe for concurrent reads after construction.
/// </summary>
internal sealed class ClrTypeDefinition : ITypeDefinition {
    private readonly FrozenDictionary<string, FrozenSet<ClrTypeMember>> _membersByName;

    public ClrTypeDefinition(Type type, ClrTypeDefinitionRegistry provider)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(provider);

        Type = type;
        BaseType = GetBaseTypeResolver(type, provider);
        Interfaces = GetInterfacesResolver(type, provider);
        GenericParameters = BuildGenericParameterCollection(type, provider);
        Fields = BuildFieldCollection(type, this, provider);
        Properties = BuildPropertyCollection(type, this, provider);
        Methods = BuildMethodCollection(type, this, provider);
        Members = BuildMemberCollection(Fields, Properties, Methods);
        _membersByName = BuildMemberDictionary(Members);
    }

    public string Name => Type.Name;
    public string? Namespace => Type.Namespace;
    public string FullName => Type.FullName ?? Type.Name;
    public Type Type { get; }
    public ClrTypeDefinition? BaseType { get; }
    public FrozenSet<ClrTypeDefinition> Interfaces { get; }
    public FrozenSet<ClrParameter> GenericParameters { get; }
    public FrozenSet<ClrTypeField> Fields { get; }
    public FrozenSet<ClrTypeProperty> Properties { get; }
    public FrozenSet<ClrMethod> Methods { get; }
    public FrozenSet<ClrTypeMember> Members { get; }

    ITypeDefinition? ITypeDefinition.BaseType => BaseType;
    IEnumerable<ITypeDefinition> ITypeDefinition.Interfaces => Interfaces;
    IEnumerable<ITypeField> ITypeDefinition.Fields => Fields;
    IEnumerable<ITypeProperty> ITypeDefinition.Properties => Properties;
    IEnumerable<ITypeMethod> ITypeDefinition.Methods => Methods;
    IEnumerable<ITypeMember> ITypeDefinition.Members => Members;
    Type ITypeDefinition.ReflectedType => Type;
    IEnumerable<IParameter> ITypeDefinition.GenericParameters => GenericParameters;

    public override string ToString() => FullName;

    private static readonly BindingFlags MemberSearchCriteria = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static FrozenSet<ClrTypeField> BuildFieldCollection(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(provider);

        var fields = type
            .GetFields(MemberSearchCriteria)
            .Select(ConstructMemberField)
            .ToFrozenSet();

        return fields;

        ClrTypeField ConstructMemberField(FieldInfo fi)
        {
            ArgumentNullException.ThrowIfNull(fi);
            ArgumentNullException.ThrowIfNull(fi.FieldType);
            ArgumentException.ThrowIfNullOrWhiteSpace(fi.Name);

            Lazy<ClrTypeDefinition> type = provider.GetDeferredTypeDefinitionResolver(fi.FieldType);
            return new ClrTypeField(type, declaringType, fi);
        }
    }


    private static FrozenSet<ClrTypeProperty> BuildPropertyCollection(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(provider);

        var properties = type.GetProperties(MemberSearchCriteria)
            .Select(ConstructMemberProperty)
            .ToFrozenSet();

        return properties;

        ClrTypeProperty ConstructMemberProperty(PropertyInfo pi)
        {
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


    private static FrozenSet<ClrMethod> BuildMethodCollection(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(provider);

        var methods = type
            .GetMethods(MemberSearchCriteria)
            .Where(mi => !mi.IsSpecialName) // Exclude property accessors and other special methods
            .Select(ConstructMethod)
            .ToFrozenSet();

        return methods;

        ClrMethod ConstructMethod(MethodInfo mi)
        {
            ArgumentNullException.ThrowIfNull(mi);

            Lazy<ClrTypeDefinition> returnType = provider.GetDeferredTypeDefinitionResolver(mi.ReturnType);
            IEnumerable<ClrParameter> parameters = mi.GetParameters().Select(pi => ConstructParameter(provider, pi)).ToArray();
            return new ClrMethod(returnType, declaringType, parameters, mi);
        }
    }

    private static ClrParameter ConstructParameter(ClrTypeDefinitionRegistry provider, ParameterInfo pi)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(pi);

        // Array indexers and some built-in methods may have null parameter names
        string parameterName = pi.Name ?? $"param{pi.Position}";

        Lazy<ClrTypeDefinition> type = provider.GetDeferredTypeDefinitionResolver(pi.ParameterType);
        return new ClrParameter(parameterName, type, pi.Position, pi.IsOptional, pi.DefaultValue);
    }

    private static FrozenSet<ClrTypeMember> BuildMemberCollection(
        IEnumerable<ClrTypeField> fields,
        IEnumerable<ClrTypeProperty> properties,
        IEnumerable<ClrMethod> methods
    )
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(methods);

        return Enumerable.Empty<ClrTypeMember>()
            .Concat(fields)
            .Concat(properties)
            .Concat(methods)
            .ToFrozenSet();
    }

    private static FrozenDictionary<string, FrozenSet<ClrTypeMember>> BuildMemberDictionary(
        IEnumerable<ClrTypeMember> members
    )
    {
        ArgumentNullException.ThrowIfNull(members);
        return members
            .GroupBy(m => m.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToFrozenSet());
    }

    private static FrozenSet<ClrParameter> BuildGenericParameterCollection(Type type, ClrTypeDefinitionRegistry provider)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(provider);

        if (!type.IsGenericType) return FrozenSet<ClrParameter>.Empty;

        var args = type.GetGenericArguments();
        Type[] namesSource;
        if (type.IsGenericTypeDefinition) {
            namesSource = args; // placeholders
        }
        else {
            namesSource = type.GetGenericTypeDefinition().GetGenericArguments();
        }

        var parameters = new List<ClrParameter>(args.Length);
        for (int i = 0; i < args.Length; i++) {
            var paramName = namesSource[i].Name;
            var lazyType = provider.GetDeferredTypeDefinitionResolver(args[i]);
            parameters.Add(new ClrParameter(paramName, lazyType, i, isOptional: false, defaultValue: null));
        }

        return parameters.ToFrozenSet();
    }

    private static ClrTypeDefinition? GetBaseTypeResolver(Type type, ClrTypeDefinitionRegistry provider)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(provider);

        if (type.IsGenericType && !type.IsGenericTypeDefinition) {
            var genericDef = type.GetGenericTypeDefinition();
            return provider.GetTypeDefinition(genericDef)!;
        }
        else if (type.BaseType != null) {
            return provider.GetTypeDefinition(type.BaseType)!;
        }
        return default;
    }



    private static FrozenSet<ClrTypeDefinition> GetInterfacesResolver(Type type, ClrTypeDefinitionRegistry provider)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(provider);

        var interfaces = type.GetInterfaces();
        return interfaces.Select(provider.GetTypeDefinition).ToFrozenSet();
    }
}