using System.Reflection;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// CLR-backed implementation of <see cref="ITypeDefinition"/> that uses reflection to surface
/// fields, properties, and methods, with immutable frozen collections for fast lookups.
/// Thread-safe for concurrent reads after construction.
/// </summary>
internal sealed class ClrTypeDefinition : IClrTypeDefinition {
    private readonly Lazy<IReadOnlyList<ClrTypeField>> _fields;
    private readonly Lazy<IReadOnlyList<ClrPropertyMember>> _properties;
    private readonly Lazy<IReadOnlyList<ClrMethod>> _methods;
    private readonly Lazy<IReadOnlyList<ClrConstructor>> _constructors;
    private readonly Lazy<IReadOnlyList<ClrTypeMember>> _members;

    private readonly ClrTypeDefinitionRegistry _provider;

    public ClrTypeDefinition(Type type, ClrTypeDefinitionRegistry provider) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(provider);

        _provider = provider;
        RuntimeType = type;
        BaseType = GetBaseTypeResolver(type, provider);
        Interfaces = GetInterfacesResolver(type, provider);
        GenericParameters = BuildGenericParameterCollection(type, provider);
        var declaredFields = BuildDeclaredFieldCollection(type, this, provider);
        var declaredProperties = BuildDeclaredPropertyCollection(type, this, provider);
        var declaredMethods = BuildDeclaredMethodCollection(type, this, provider);

        _fields = new(() => ComposeMemberCollection(
            declaredFields,
            BaseType,
            Interfaces,
            includeInterfaceMembers: false,
            static typeDefinition => typeDefinition.Fields,
            static field => $"{field.Name}|{field.LifetimeModifier}"));
        _properties = new(() => ComposeMemberCollection(
            declaredProperties,
            BaseType,
            Interfaces,
            includeInterfaceMembers: RuntimeType.IsInterface,
            static typeDefinition => typeDefinition.Properties,
            GetPropertySignature,
            property => !RuntimeType.IsArray || property.DeclaringTypeDefinition == this || !property.Parameters.Any()));
        _methods = new(() => ComposeMemberCollection(
            declaredMethods,
            BaseType,
            Interfaces,
            includeInterfaceMembers: RuntimeType.IsInterface,
            static typeDefinition => typeDefinition.Methods,
            GetMethodSignature));
        _constructors = new(() => BuildConstructorCollection(type, this, provider));
        _members = new(() => BuildMemberCollection(Fields, Properties, Methods));
    }

    public string Name => RuntimeType.Name;
    public string? Namespace => RuntimeType.Namespace;
    public string FullName => RuntimeType.FullName ?? RuntimeType.Name;
    public AccessModifier AccessModifier => ClrAccessModifierResolver.Resolve(RuntimeType);
    public Type RuntimeType { get; }
    public ClrTypeDefinition? BaseType { get; }
    public IReadOnlyList<ClrTypeDefinition> Interfaces { get; }
    public IReadOnlyList<ClrParameter> GenericParameters { get; }
    public IReadOnlyList<ClrTypeField> Fields => _fields.Value;
    public IReadOnlyList<ClrPropertyMember> Properties => _properties.Value;
    public IReadOnlyList<ClrMethod> Methods => _methods.Value;
    public IReadOnlyList<ClrConstructor> Constructors => _constructors.Value;
    public IReadOnlyList<ClrTypeMember> Members => _members.Value;

    ITypeDefinition? ITypeDefinition.BaseType => BaseType;
    IEnumerable<ITypeDefinition> ITypeDefinition.Interfaces => Interfaces;
    IEnumerable<ITypeField> ITypeDefinition.Fields => Fields;
    IEnumerable<ITypeProperty> ITypeDefinition.Properties => Properties;
    IEnumerable<ITypeMethod> ITypeDefinition.Methods => Methods;
    IEnumerable<ITypeConstructor> ITypeDefinition.Constructors => Constructors;
    IEnumerable<ITypeMember> ITypeDefinition.Members => Members;
    IEnumerable<IParameter> ITypeDefinition.GenericParameters => GenericParameters; PrimitiveType? ITypeDefinition.PrimitiveType => GetPrimitiveTypeId(RuntimeType);
    TypeCategory ITypeDefinition.TypeCategory => GetTypeCategory(RuntimeType);
    public override string ToString() => FullName;

    internal ClrMethod? FindConversionFrom(ClrTypeDefinition source, ConversionOperatorKind kind) {
        ArgumentNullException.ThrowIfNull(source);
        var name = kind is ConversionOperatorKind.Implicit ? "op_Implicit" : "op_Explicit";
        var from = source.RuntimeType;
        var to = RuntimeType;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        foreach (var declaring in new[] { from, to }) {
            foreach (var method in declaring.GetMethods(flags)) {
                if (!method.IsSpecialName || method.Name != name)
                    continue;
                var parameters = method.GetParameters();
                if (parameters.Length != 1
                    || method.ReturnType != to
                    || parameters[0].ParameterType != from)
                    continue;
                return CreateMethod(method);
            }
        }
        return null;
    }

    private ClrMethod CreateMethod(MethodInfo methodInfo) {
        var declaringType = methodInfo.DeclaringType == RuntimeType
            ? this
            : _provider.GetTypeDefinition(methodInfo.DeclaringType!);
        var returnType = _provider.GetDeferredTypeDefinitionResolver(methodInfo.ReturnType);
        var parameters = methodInfo.GetParameters().Select(pi => ConstructParameter(_provider, pi));
        return new ClrMethod(returnType, declaringType, parameters, methodInfo);
    }

    private static readonly BindingFlags MemberSearchCriteria = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly BindingFlags DeclaredMemberSearchCriteria = MemberSearchCriteria | BindingFlags.DeclaredOnly;

    private static IReadOnlyList<ClrTypeField> BuildDeclaredFieldCollection(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(provider);

        var fields = type
            .GetFields(DeclaredMemberSearchCriteria)
            .Select(ConstructMemberField)
            .ToArray();

        return fields;

        ClrTypeField ConstructMemberField(FieldInfo fi) {
            ArgumentNullException.ThrowIfNull(fi);
            ArgumentNullException.ThrowIfNull(fi.FieldType);
            ArgumentException.ThrowIfNullOrWhiteSpace(fi.Name);

            Lazy<ClrTypeDefinition> type = provider.GetDeferredTypeDefinitionResolver(fi.FieldType);
            return new ClrTypeField(type, declaringType, fi);
        }
    }


    private static IReadOnlyList<ClrPropertyMember> BuildDeclaredPropertyCollection(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(provider);

        var properties = type.GetProperties(DeclaredMemberSearchCriteria)
            .Select(ConstructMemberProperty)
            .Concat(BuildSyntheticProperties(type, declaringType, provider))
            .GroupBy(GetPropertySignature)
            .Select(static group => group.First())
            .ToArray();

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

        static IEnumerable<ClrPropertyMember> BuildSyntheticProperties(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider) {
            if (!type.IsArray) {
                return [];
            }

            var elementType = type.GetElementType();
            if (elementType is null) {
                return [];
            }

            var elementTypeResolver = provider.GetDeferredTypeDefinitionResolver(elementType);
            var intTypeResolver = provider.GetDeferredTypeDefinitionResolver(typeof(int));
            var parameters = Enumerable.Range(0, type.GetArrayRank())
                .Select(index => new ClrParameter($"index{index}", intTypeResolver, index, isOptional: false, defaultValue: null))
                .ToArray();

            return [new ClrTypeSyntheticProperty(elementTypeResolver, declaringType, parameters, "Item", isStatic: false)];
        }
    }


    private static IReadOnlyList<ClrMethod> BuildDeclaredMethodCollection(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(provider);

        var methods = type
            .GetMethods(DeclaredMemberSearchCriteria)
            .Where(mi => !mi.IsSpecialName) // Exclude property accessors and other special methods
            .Select(ConstructMethod)
            .ToArray();

        return methods;

        ClrMethod ConstructMethod(MethodInfo mi) {
            ArgumentNullException.ThrowIfNull(mi);

            Lazy<ClrTypeDefinition> returnType = provider.GetDeferredTypeDefinitionResolver(mi.ReturnType);
            IEnumerable<ClrParameter> parameters = mi.GetParameters().Select(pi => ConstructParameter(provider, pi)).ToArray();
            return new ClrMethod(returnType, declaringType, parameters, mi);
        }
    }

    private static IReadOnlyList<ClrConstructor> BuildConstructorCollection(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(provider);

        var constructors = type
            .GetConstructors(DeclaredMemberSearchCriteria)
            .Select(ConstructConstructor)
            .ToArray();

        return constructors;

        ClrConstructor ConstructConstructor(ConstructorInfo constructorInfo) {
            ArgumentNullException.ThrowIfNull(constructorInfo);

            IEnumerable<ClrParameter> parameters = constructorInfo
                .GetParameters()
                .Select(pi => ConstructParameter(provider, pi))
                .ToArray();

            return new ClrConstructor(declaringType, parameters, constructorInfo);
        }
    }

    private static ClrParameter ConstructParameter(ClrTypeDefinitionRegistry provider, ParameterInfo pi) {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(pi);

        // Array indexers and some built-in methods may have null parameter names
        string parameterName = pi.Name ?? $"param{pi.Position}";

        Lazy<ClrTypeDefinition> type = provider.GetDeferredTypeDefinitionResolver(pi.ParameterType);
        return new ClrParameter(parameterName, type, pi.Position, pi.IsOptional, pi.DefaultValue);
    }

    private static IReadOnlyList<TMember> ComposeMemberCollection<TMember>(
        IEnumerable<TMember> declaredMembers,
        ClrTypeDefinition? baseType,
        IEnumerable<ClrTypeDefinition> interfaces,
        bool includeInterfaceMembers,
        Func<ClrTypeDefinition, IEnumerable<TMember>> inheritedMemberSelector,
        Func<TMember, string> keySelector,
        Func<TMember, bool>? inheritedMemberFilter = null
    ) where TMember : ITypeMember {
        ArgumentNullException.ThrowIfNull(declaredMembers);
        ArgumentNullException.ThrowIfNull(interfaces);
        ArgumentNullException.ThrowIfNull(inheritedMemberSelector);
        ArgumentNullException.ThrowIfNull(keySelector);

        var members = new List<TMember>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        AddMembers(declaredMembers);

        if (baseType is not null) {
            AddMembers(inheritedMemberSelector(baseType), inheritedMemberFilter);
        }

        if (includeInterfaceMembers) {
            foreach (var implementedInterface in interfaces) {
                AddMembers(inheritedMemberSelector(implementedInterface), inheritedMemberFilter);
            }
        }

        return members;

        void AddMembers(IEnumerable<TMember> source, Func<TMember, bool>? filter = null) {
            foreach (var member in source) {
                if (filter is not null && !filter(member)) {
                    continue;
                }

                if (seen.Add(keySelector(member))) {
                    members.Add(member);
                }
            }
        }
    }

    private static IReadOnlyList<ClrTypeMember> BuildMemberCollection(
        IEnumerable<ClrTypeField> fields,
        IEnumerable<ClrPropertyMember> properties,
        IEnumerable<ClrMethod> methods
    ) {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(methods);

        return Enumerable.Empty<ClrTypeMember>()
            .Concat(fields)
            .Concat(properties)
            .Concat(methods)
            .ToArray();
    }

    private static IReadOnlyList<ClrParameter> BuildGenericParameterCollection(Type type, ClrTypeDefinitionRegistry provider) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(provider);

        if (!type.IsGenericType) return [];

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

        return parameters;
    }

    private static ClrTypeDefinition? GetBaseTypeResolver(Type type, ClrTypeDefinitionRegistry provider) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(provider);

        if (type.BaseType != null) {
            return provider.GetTypeDefinition(type.BaseType)!;
        }
        return default;
    }



    private static IReadOnlyList<ClrTypeDefinition> GetInterfacesResolver(Type type, ClrTypeDefinitionRegistry provider) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(provider);

        var interfaces = type.GetInterfaces();
        return interfaces.Select(provider.GetTypeDefinition).ToArray();
    }

    private static string GetMethodSignature(ClrMethod method) {
        ArgumentNullException.ThrowIfNull(method);
        return $"{method.Name}|{method.LifetimeModifier}|{GetParameterSignature(method.Parameters)}";
    }

    private static string GetPropertySignature(ClrPropertyMember property) {
        ArgumentNullException.ThrowIfNull(property);
        var propertyName = property.Name.EndsWith(".Item", StringComparison.Ordinal) ? "Item" : property.Name;
        return $"{propertyName}|{property.LifetimeModifier}|{GetParameterSignature(property.Parameters)}";
    }

    private static string GetParameterSignature(IEnumerable<IParameter>? parameters) {
        return parameters is null
            ? string.Empty
            : string.Join(",", parameters.Select(static parameter => parameter.ParameterTypeDefinition.FullName));
    }

    /// <summary>
    /// Delegates to the canonical <see cref="PrimitiveType.GetPrimitiveType(Type)"/>
    /// mapping. This private wrapper exists only to keep call sites unchanged; new
    /// code should call <c>type.GetPrimitiveType()</c> directly.
    /// </summary>
    private static PrimitiveType? GetPrimitiveTypeId(Type type) =>
        type.GetPrimitiveType();

    private static TypeCategory GetTypeCategory(Type type) {
        ArgumentNullException.ThrowIfNull(type);

        var primitiveId = GetPrimitiveTypeId(type);
        if (primitiveId.HasValue) {
            return primitiveId.Value.GetCategory();
        }

        // Handle complex types
        if (type.IsArray || type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))) {
            return TypeCategory.Collection;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>)) {
            return TypeCategory.Nullable;
        }

        if (type.IsEnum) {
            return TypeCategory.Enumeration;
        }

        if (type.IsClass || type.IsInterface) {
            return TypeCategory.None; // Complex reference types
        }

        return TypeCategory.None;
    }
}