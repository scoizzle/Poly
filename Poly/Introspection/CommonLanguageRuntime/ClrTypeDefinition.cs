using System.Collections.Frozen;
using System.Reflection;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// CLR-backed implementation of <see cref="ITypeDefinition"/> that uses reflection to surface
/// fields, properties, and methods, with immutable frozen collections for fast lookups.
/// Thread-safe for concurrent reads after construction.
/// </summary>
internal sealed class ClrTypeDefinition : IClrTypeDefinition {
    public ClrTypeDefinition(Type type, ClrTypeDefinitionRegistry provider) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(provider);

        RuntimeType = type;
        BaseType = GetBaseTypeResolver(type, provider);
        Interfaces = GetInterfacesResolver(type, provider);
        GenericParameters = BuildGenericParameterCollection(type, provider);
        Fields = BuildFieldCollection(type, this, provider);
        Properties = BuildPropertyCollection(type, this, provider);
        Methods = BuildMethodCollection(type, this, provider);
        Constructors = BuildConstructorCollection(type, this, provider);
        Members = BuildMemberCollection(Fields, Properties, Methods);
    }

    public string Name => RuntimeType.Name;
    public string? Namespace => RuntimeType.Namespace;
    public string FullName => RuntimeType.FullName ?? RuntimeType.Name;
    public Type RuntimeType { get; }
    public ClrTypeDefinition? BaseType { get; }
    public FrozenSet<ClrTypeDefinition> Interfaces { get; }
    public FrozenSet<ClrParameter> GenericParameters { get; }
    public FrozenSet<ClrTypeField> Fields { get; }
    public FrozenSet<ClrPropertyMember> Properties { get; }
    public FrozenSet<ClrMethod> Methods { get; }
    public FrozenSet<ClrConstructor> Constructors { get; }
    public FrozenSet<ClrTypeMember> Members { get; }

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

    private static readonly BindingFlags MemberSearchCriteria = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static FrozenSet<ClrTypeField> BuildFieldCollection(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(provider);

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


    private static FrozenSet<ClrPropertyMember> BuildPropertyCollection(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(provider);

        var properties = type.GetProperties(MemberSearchCriteria)
            .Select(ConstructMemberProperty)
            .Concat(BuildSyntheticProperties(type, declaringType, provider))
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


    private static FrozenSet<ClrMethod> BuildMethodCollection(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(provider);

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

    private static FrozenSet<ClrConstructor> BuildConstructorCollection(Type type, ClrTypeDefinition declaringType, ClrTypeDefinitionRegistry provider) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(provider);

        var constructors = type
            .GetConstructors(MemberSearchCriteria)
            .Select(ConstructConstructor)
            .ToFrozenSet();

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

    private static FrozenSet<ClrTypeMember> BuildMemberCollection(
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
            .ToFrozenSet();
    }

    private static FrozenDictionary<string, FrozenSet<ClrTypeMember>> BuildMemberDictionary(
        IEnumerable<ClrTypeMember> members
    ) {
        ArgumentNullException.ThrowIfNull(members);
        return members
            .GroupBy(m => m.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToFrozenSet());
    }

    private static FrozenSet<ClrParameter> BuildGenericParameterCollection(Type type, ClrTypeDefinitionRegistry provider) {
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

    private static ClrTypeDefinition? GetBaseTypeResolver(Type type, ClrTypeDefinitionRegistry provider) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(provider);

        if (type.BaseType != null) {
            return provider.GetTypeDefinition(type.BaseType)!;
        }
        return default;
    }



    private static FrozenSet<ClrTypeDefinition> GetInterfacesResolver(Type type, ClrTypeDefinitionRegistry provider) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(provider);

        var interfaces = type.GetInterfaces();
        return interfaces.Select(provider.GetTypeDefinition).ToFrozenSet();
    }

    private static PrimitiveType? GetPrimitiveTypeId(Type type) {
        ArgumentNullException.ThrowIfNull(type);

        return type switch {
            Type t when t == typeof(bool) => PrimitiveType.Boolean,
            Type t when t == typeof(sbyte) => PrimitiveType.Int8,
            Type t when t == typeof(short) => PrimitiveType.Int16,
            Type t when t == typeof(int) => PrimitiveType.Int32,
            Type t when t == typeof(long) => PrimitiveType.Int64,
            Type t when t == typeof(byte) => PrimitiveType.UInt8,
            Type t when t == typeof(ushort) => PrimitiveType.UInt16,
            Type t when t == typeof(uint) => PrimitiveType.UInt32,
            Type t when t == typeof(ulong) => PrimitiveType.UInt64,
            Type t when t == typeof(float) => PrimitiveType.Float32,
            Type t when t == typeof(double) => PrimitiveType.Float64,
            Type t when t == typeof(decimal) => PrimitiveType.Decimal,
            Type t when t == typeof(string) => PrimitiveType.String,
            Type t when t == typeof(char) => PrimitiveType.Char,
            Type t when t == typeof(DateTime) => PrimitiveType.DateTime,
            Type t when t == typeof(DateOnly) => PrimitiveType.DateOnly,
            Type t when t == typeof(TimeOnly) => PrimitiveType.TimeOnly,
            Type t when t == typeof(TimeSpan) => PrimitiveType.TimeSpan,
            Type t when t == typeof(Guid) => PrimitiveType.Guid,
            Type t when t == typeof(byte[]) => PrimitiveType.ByteArray,
            _ => null
        };
    }

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