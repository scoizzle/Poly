using System.Collections.Concurrent;
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
    private readonly Lazy<IReadOnlyList<ClrTypeOperator>> _operators;
    private readonly Lazy<IReadOnlyList<ITypeConversion>> _conversions;

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
        // Declared only (owner ruling 1, #998 T10): the reflected op_* members; C#'s built-ins are its type system's, and
        // C# 11 does not consult a native integer's own members.
        _operators = new(() => Poly.Interpretation.CSharp.CSharpBuiltIns.IsNativeInteger(RuntimeType)
            ? []
            : [.. Enum.GetValues<OperatorKind>().SelectMany(kind => FindOperators(kind).Select(method => new ClrTypeOperator(kind, method)))]);
        _conversions = new(ReadConversions);
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

    /// <summary>
    /// The operators this type declares: its reflected static <c>op_*</c> members, none for a native integer (#998 T10,
    /// owner ruling 1). C#'s built-in operators are its type system's.
    /// </summary>
    public IReadOnlyList<ClrTypeOperator> Operators => _operators.Value;

    /// <summary>A CLR type is its definition instance: the registry builds one per runtime type and hands it out.</summary>
    /// <summary>C# over the CLR: the CLR's definitions are cached process-wide, so they belong to its one shared system
    /// (#998 T9, decision §2.4).</summary>
    public ITypeSystem System => Poly.Interpretation.CSharp.CSharpTypeSystem.Shared;

    /// <summary>
    /// A CLR sequence shape: an array or a closed <c>IEnumerable&lt;&gt;</c>, and not itself a primitive (so
    /// <c>byte[]</c> stays binary).
    /// </summary>
    internal bool IsCollectionShape =>
        RuntimeType != typeof(string) && RuntimeType != typeof(byte[])
        && (RuntimeType.IsArray
            || RuntimeType.GetInterfaces().Any(static i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)));

    /// <summary>The element a CLR sequence yields: the array's element, the closed <c>IEnumerable&lt;&gt;</c>'s argument,
    /// or <c>object</c> for a non-generic sequence.</summary>
    internal ITypeDefinition? SequenceElementType {
        get {
            var enumerableType = RuntimeType.IsArray
                ? null
                : RuntimeType.GetInterfaces().FirstOrDefault(static i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            if (RuntimeType.IsArray)
                return _provider.GetTypeDefinition(RuntimeType.GetElementType()!);
            if (enumerableType is not null)
                return _provider.GetTypeDefinition(enumerableType.GetGenericArguments()[0]);
            return typeof(IEnumerable).IsAssignableFrom(RuntimeType)
                ? _provider.GetTypeDefinition(typeof(object))
                : null;
        }
    }

    /// <summary>
    /// The conversions this type declares: the reflected static <c>op_Implicit</c>/<c>op_Explicit</c> members (either
    /// end). C#'s built-ins are its type system's (#998 T10).
    /// </summary>
    internal IReadOnlyList<ITypeConversion> Conversions => _conversions.Value;

    ITypeDefinition? ITypeDefinition.BaseType => BaseType;
    IEnumerable<ITypeDefinition> ITypeDefinition.Interfaces => Interfaces;
    IEnumerable<ITypeField> ITypeDefinition.Fields => Fields;
    IEnumerable<ITypeProperty> ITypeDefinition.Properties => Properties;
    IEnumerable<ITypeMethod> ITypeDefinition.Methods => Methods;
    IEnumerable<ITypeConstructor> ITypeDefinition.Constructors => Constructors;
    IEnumerable<ITypeMember> ITypeDefinition.Members => Members;
    IEnumerable<ITypeOperator> ITypeDefinition.Operators => Operators;
    IEnumerable<ITypeConversion> ITypeDefinition.Conversions => Conversions;
    IEnumerable<IParameter> ITypeDefinition.GenericParameters => GenericParameters;

    /// <summary>A CLR nullable form is <c>Nullable&lt;T&gt;</c>; every other definition wraps nothing. C#'s system asks
    /// it (#998 T9: the CLR host's own relation, not a member of the contract).</summary>
    internal ITypeDefinition? NullableOf =>
        Nullable.GetUnderlyingType(RuntimeType) is { } underlying
            ? _provider.GetTypeDefinition(underlying)
            : null;
    /// <summary>The registry that built this definition (C#'s built-in rules make their operators from it).</summary>
    internal ClrTypeDefinitionRegistry Provider => _provider;

    public override string ToString() => FullName;

    /// <summary>
    /// The conversion of <paramref name="kind"/> declared on the source or on this type, in that order, read from each
    /// definition's <see cref="Conversions"/> and matched on the ends the item names. A modeled source declares its
    /// own, so this answers it without naming a spelling.
    /// </summary>
    internal ITypeConversion? FindDeclaredConversion(ITypeDefinition source, ConversionOperatorKind kind) {
        foreach (var candidate in new[] { source, (ITypeDefinition)this }) {
            foreach (var conversion in candidate.Conversions) {
                if (conversion.Kind != kind || conversion.Method is null)
                    continue;
                if (TypeDefinitionExtensions.IsSameType(conversion.SourceTypeDefinition, source)
                    && TypeDefinitionExtensions.IsSameType(conversion.ResultTypeDefinition, this))
                    return conversion;
            }
        }
        return null;
    }

    /// <summary>
    /// The CLR's own assignability: <see cref="Type.IsAssignableFrom"/> between two CLR definitions (inheritance,
    /// interfaces, variance, array covariance), the lift from <c>T</c> to <c>T?</c> that C# applies and the CLR does
    /// not, and <c>object</c> accepting every definition, modeled ones included: the VM boxes a module entity as
    /// <c>object</c> (<c>DomainResult.Success</c>). That last rule is also stated by <see cref="ClrBuiltInRules"/> as
    /// the implicit conversion into <c>object</c>, but only between two CLR definitions. It stays here as well because
    /// it is what makes <c>object</c> a tier 1 parameter, and moving it to the conversion answer alone would score
    /// <c>F(object)</c> against <c>F(long)</c> differently and change which overload wins; the two statements agree on
    /// every CLR definition (<c>ObjectAcceptsEveryClrDefinition_IsAssignableFromAndResolveConversionFromAgree</c>).
    /// </summary>
    /// <remarks>The CLR host's own assignability body; C#'s system asks it (#998 T9).</remarks>
    internal bool AssignsWithoutConversion(ITypeDefinition source) {
        ArgumentNullException.ThrowIfNull(source);
        if (RuntimeType == typeof(object))
            return true;
        if (source is ClrTypeDefinition clrSource
            && (RuntimeType.IsAssignableFrom(clrSource.RuntimeType)
                || Nullable.GetUnderlyingType(RuntimeType) == clrSource.RuntimeType))
            return true;
        return TypeDefinitionExtensions.IsAssignableByHierarchy(this, source);
    }

    internal IEnumerable<ClrMethod> FindOperators(OperatorKind kind) {
        if (ClrSpecialNames.OperatorName(kind) is not { } name)
            return [];
        var arity = OperatorShape.ArityOf(kind);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        return [.. RuntimeType.GetMethods(flags)
            .Where(method => method.IsSpecialName && method.Name == name && method.GetParameters().Length == arity)
            .Select(CreateMethod)];
    }

    /// <summary>
    /// The conversions this type declares (owner ruling 1, #998 T10): each reflected static special-name
    /// <c>op_Implicit</c>/<c>op_Explicit</c> member with one parameter. C#'s built-in conversions are its type system's;
    /// a declared conversion the other end owns is not listed, and C#'s <c>Conversion</c> finds it.
    /// </summary>
    private IReadOnlyList<ITypeConversion> ReadConversions() {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        var stated = new List<ITypeConversion>();
        foreach (var method in RuntimeType.GetMethods(flags)) {
            if (!method.IsSpecialName || method.GetParameters().Length != 1)
                continue;
            if (ClrSpecialNames.ConversionKindOf(method.Name) is not { } kind)
                continue;
            stated.Add(ClrTypeConversion.Declared(kind, CreateMethod(method)));
        }
        return stated;
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
}