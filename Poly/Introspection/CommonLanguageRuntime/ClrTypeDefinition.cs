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
    private readonly ConcurrentDictionary<(OperatorKind Kind, PrimitiveType? Other), IReadOnlyList<ClrTypeOperator>> _operatorsBeside = new();
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
        _operators = new(() => [.. Enum.GetValues<OperatorKind>().SelectMany(kind => OperatorsBeside(kind, null))]);
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
    /// The operators that apply to two operands of this type (one, for a unary kind): what <see cref="OperatorsBeside"/>
    /// offers beside this type itself, for every kind. A promoted operator is declared by the type it promotes to.
    /// </summary>
    public IReadOnlyList<ClrTypeOperator> Operators => _operators.Value;

    /// <summary>A CLR type is its definition instance: the registry builds one per runtime type and hands it out.</summary>
    bool ITypeDefinition.IsSameTypeAs(ITypeDefinition other) => ReferenceEquals(this, other);

    /// <summary>
    /// A CLR sequence shape: an array or a closed <c>IEnumerable&lt;&gt;</c>, and not itself a primitive (so
    /// <c>byte[]</c> stays binary).
    /// </summary>
    bool ITypeDefinition.IsCollectionShape =>
        ((ITypeDefinition)this).PrimitiveType is null
        && (RuntimeType.IsArray
            || RuntimeType.GetInterfaces().Any(static i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)));

    /// <summary>The element a CLR sequence yields: the array's element, the closed <c>IEnumerable&lt;&gt;</c>'s argument,
    /// or <c>object</c> for a non-generic sequence.</summary>
    ITypeDefinition? ITypeDefinition.SequenceElementType {
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
    /// The conversions this type states: the reflected static <c>op_Implicit</c>/<c>op_Explicit</c> members it declares
    /// (either end) and the built-in rules with this type as the source, which name no method. Reference conversions
    /// are assignability, not conversion, and the lift into a nullable target is the caller's (T-06), so neither is
    /// listed.
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
    ITypeOperator? ITypeDefinition.ResolveOperator(OperatorKind kind, ITypeDefinition? right) => ClrOperatorResolution.Resolve(this, kind, right);
    IEnumerable<IParameter> ITypeDefinition.GenericParameters => GenericParameters;
    PrimitiveType? ITypeDefinition.PrimitiveType => GetPrimitiveTypeId(RuntimeType);

    /// <summary>The CLR maps every primitive it has into the shared vocabulary, so it states no atom.</summary>
    PrimitiveAtom? ITypeDefinition.PlatformAtom => null;

    /// <summary>A CLR nullable form is <c>Nullable&lt;T&gt;</c>; every other definition wraps nothing.</summary>
    ITypeDefinition? ITypeDefinition.NullableOf =>
        Nullable.GetUnderlyingType(RuntimeType) is { } underlying
            ? _provider.GetTypeDefinition(underlying)
            : null;
    public TypeKind Kind => GetTypeKind(RuntimeType);
    public override string ToString() => FullName;

    /// <summary>
    /// The declared conversion first (implicit preferred), so a conversion that names a method is never replaced by a
    /// built-in rule; then the built-in rules, only between two CLR definitions. A native integer's implicit built-in
    /// conversion answers first: C# 11 makes <c>nint</c> to <c>long</c> implicit where IntPtr declares an explicit
    /// <c>op_Explicit</c>; an explicit one keeps IntPtr's declared method (<c>(int)nint</c>).
    /// </summary>
    ITypeConversion? ITypeDefinition.ResolveConversionFrom(ITypeDefinition source) {
        ArgumentNullException.ThrowIfNull(source);
        var clrSource = source as ClrTypeDefinition;
        ConversionOperatorKind? builtInKind = null;
        if (clrSource is not null) {
            builtInKind = ClrBuiltInRules.Conversion(GetPrimitiveTypeId(clrSource.RuntimeType), GetPrimitiveTypeId(RuntimeType));
            if (builtInKind is ConversionOperatorKind.Implicit
                && (ClrBuiltInRules.IsNativeInteger(clrSource.RuntimeType) || ClrBuiltInRules.IsNativeInteger(RuntimeType)))
                return ClrTypeConversion.Intrinsic(builtInKind.Value, source, this);
        }
        if (FindDeclaredConversion(source, ConversionOperatorKind.Implicit) is { } declaredImplicit)
            return declaredImplicit;
        if (FindDeclaredConversion(source, ConversionOperatorKind.Explicit) is { } declaredExplicit)
            return declaredExplicit;
        if (builtInKind is { } kind)
            return ClrTypeConversion.Intrinsic(kind, source, this);
        return clrSource is null ? null : ResolveLiftedImplicitFrom(clrSource);
    }

    /// <summary>
    /// The conversion of <paramref name="kind"/> declared on the source or on this type, in that order, read from each
    /// definition's <see cref="Conversions"/> and matched on the ends the item names. A modeled source declares its
    /// own, so this answers it without naming a spelling.
    /// </summary>
    private ITypeConversion? FindDeclaredConversion(ITypeDefinition source, ConversionOperatorKind kind) {
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
    /// C# 10.6.1's implicit nullable conversion: into <c>U?</c> from <c>S</c> or <c>S?</c> where <c>S</c> converts to
    /// <c>U</c> implicitly (<c>long</c> to <c>decimal?</c>, <c>int?</c> to <c>long?</c>). It names no method, whatever the
    /// underlying conversion declares, so it is never assignability; the emitter applies it from the bound parameter type.
    /// Only for a nullable target. <c>S</c> to <c>S?</c> is the lift assignability states.
    /// </summary>
    private ITypeConversion? ResolveLiftedImplicitFrom(ClrTypeDefinition source) {
        var underlying = Nullable.GetUnderlyingType(RuntimeType);
        if (underlying is null)
            return null;
        var sourceUnderlying = Nullable.GetUnderlyingType(source.RuntimeType) ?? source.RuntimeType;
        if (sourceUnderlying == underlying)
            return null;
        var underlyingConversion = ((ITypeDefinition)_provider.GetTypeDefinition(underlying))
            .ResolveConversionFrom(_provider.GetTypeDefinition(sourceUnderlying));
        return underlyingConversion is { Kind: ConversionOperatorKind.Implicit }
            ? ClrTypeConversion.Intrinsic(ConversionOperatorKind.Implicit, source, this)
            : null;
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
    bool ITypeDefinition.IsAssignableWithoutConversion(ITypeDefinition source) {
        ArgumentNullException.ThrowIfNull(source);
        if (RuntimeType == typeof(object))
            return true;
        if (source is ClrTypeDefinition clrSource
            && (RuntimeType.IsAssignableFrom(clrSource.RuntimeType)
                || Nullable.GetUnderlyingType(RuntimeType) == clrSource.RuntimeType))
            return true;
        return TypeDefinitionExtensions.IsAssignableByHierarchy(this, source);
    }

    /// <summary>
    /// The operators this type offers beside an operand of <paramref name="other"/> (null: this type): the operators C#
    /// applies itself, then the reflected static special-name <c>op_*</c> members, except a native integer's, which C# 11
    /// does not consult. Where the CLR also declares an intrinsic's exact signature (Double and Single declare their
    /// comparisons), the intrinsic is the answer. The built-in rules read the other operand only through its primitive,
    /// so the cache is keyed by the kind and that primitive: keyed by the kind alone, the first pair would answer for
    /// every other; keyed by the other definition, it would grow without bound on fresh synthetic collection types,
    /// which are a new instance per resolve. Reference equality depends on the other definition itself, so it is
    /// answered beside the cache (<see cref="ClrBuiltInRules.ReferenceEquality"/>).
    /// </summary>
    internal IReadOnlyList<ClrTypeOperator> OperatorsBeside(OperatorKind kind, ITypeDefinition? other) {
        OperatorShape.ArityOf(kind);
        var otherPrimitive = other is null ? GetPrimitiveTypeId(RuntimeType) : other.PrimitiveType;
        var offered = _operatorsBeside.GetOrAdd((kind, otherPrimitive), key => {
            var intrinsic = ClrBuiltInRules.Operators(this, key.Other, _provider, key.Kind);
            var reflected = (ClrBuiltInRules.IsNativeInteger(RuntimeType) ? [] : FindOperators(key.Kind))
                .Where(method => !intrinsic.Any(builtIn => SameSignature(builtIn, method)))
                .Select(method => new ClrTypeOperator(key.Kind, method));
            return [.. intrinsic, .. reflected];
        });
        return ClrBuiltInRules.ReferenceEquality(this, other, _provider, kind) is { } reference ? [.. offered, reference] : offered;
    }

    private static bool SameSignature(ClrTypeOperator intrinsic, ClrMethod method) {
        var (pa, pb) = (intrinsic.Parameters.ToArray(), method.Parameters.ToArray());
        return ReferenceEquals(intrinsic.ResultTypeDefinition, method.MemberTypeDefinition)
            && pa.Length == pb.Length
            && pa.Zip(pb).All(static pair => ReferenceEquals(pair.First.ParameterTypeDefinition, pair.Second.ParameterTypeDefinition));
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
    /// The conversions this type states, declared ones first: each reflected static special-name
    /// <c>op_Implicit</c>/<c>op_Explicit</c> member with one parameter, then the built-in rule for this type as the
    /// source and each simple type. The rules table is the same one <see cref="ITypeDefinition.ResolveConversionFrom"/>
    /// reads; a declared conversion the other end owns is not listed, and is answered by the query.
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
        var from = GetPrimitiveTypeId(RuntimeType);
        foreach (var primitive in Enum.GetValues<PrimitiveType>()) {
            if (primitive == from || ClrBuiltInRules.Conversion(from, primitive) is not { } kind)
                continue;
            if (((ITypeDefinitionProvider)_provider).GetTypeDefinition(primitive) is not { } target)
                continue;
            Add(ClrTypeConversion.Intrinsic(kind, this, target));
        }
        return stated;

        void Add(ITypeConversion conversion) {
            var duplicate = stated.Any(existing =>
                TypeDefinitionExtensions.IsSameType(existing.SourceTypeDefinition, conversion.SourceTypeDefinition)
                && TypeDefinitionExtensions.IsSameType(existing.ResultTypeDefinition, conversion.ResultTypeDefinition));
            if (!duplicate)
                stated.Add(conversion);
        }
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

    private static TypeKind GetTypeKind(Type type) {
        ArgumentNullException.ThrowIfNull(type);

        if (GetPrimitiveTypeId(type) is not null)
            return TypeKind.Primitive;
        if (type.IsEnum)
            return TypeKind.Enum;
        // Nullable<T> lands here: it is a value type, not a primitive.
        if (type.IsValueType)
            return TypeKind.Struct;
        if (type.IsInterface)
            return TypeKind.Interface;
        if (typeof(Delegate).IsAssignableFrom(type))
            return TypeKind.Delegate;
        return TypeKind.Class;
    }
}