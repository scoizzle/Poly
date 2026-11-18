using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// Analyzer that extracts ITypeDefinition instances from TypeDefinitionNode AST nodes.
/// Stores the extracted type definitions in the analysis context for use by other analyzers.
/// The table belongs to one analysis; query it through <see cref="Poly.Analysis.AnalysisResult.TypeDefinitions"/>.
/// </summary>
public sealed class TypeDefinitionNodeAnalyzer : INodeAnalyzer {
    public const string Id = "TypeDefinitionNode";
    public string PassName => Id;

    public void Analyze(AnalysisContext context, Node node) {
        var registry = context.GetMetadata<AstTypeRegistry>(default);
        if (registry is null) {
            registry = new AstTypeRegistry();
            context.SetMetadata(default, registry);
            context.TypeDefinitions.Add(registry);
        }

        if (node is TypeDefinitionNode typeDef) {
            // A re-analysis of a type the caller already built must keep that
            // instance. A second Patron is not assignable to the first, so
            // this fails to match a Patron parameter. A type registered by the
            // provider passed to this analysis is that same instance. Look up
            // by node, not name: a domain can hold two definitions with the
            // same name (module and runtime shapes), and the provider's
            // precedence decides which one a bare name resolves to.
            var existing = context.GetMetadata<TypeDefinitionMetadata>(typeDef)?.TypeDefinition
                    as AstTypeDefinition
                ?? context.TypeDefinitions.GetTypeDefinition(typeDef) as AstTypeDefinition;
            if (existing is not null) {
                if (context.GetMetadata<TypeDefinitionMetadata>(typeDef) is null)
                    context.SetMetadata(typeDef, new TypeDefinitionMetadata(existing));
            }
            else {
                var definition = new AstTypeDefinition(typeDef, context.TypeDefinitions);
                registry.Add(definition);
                context.SetMetadata(node, new TypeDefinitionMetadata(definition));
            }
        }

        this.AnalyzeChildren(context, node);
    }
}

/// <summary>
/// Per-analysis AST type table. Lives on <see cref="AnalysisContext"/> so the
/// shared <see cref="Interpreter.Analyzer"/> instance cannot leak or clear
/// types across concurrent analyses.
/// </summary>
internal sealed class AstTypeRegistry : ITypeDefinitionProvider, ITypeDefinitionNodeProvider, IAnalysisMetadata {
    private readonly Dictionary<string, AstTypeDefinition> _types = new(StringComparer.Ordinal);

    /// <summary>CLR arity suffix so <c>DomainResult</c> and <c>DomainResult&lt;T&gt;</c> both stay registered.</summary>
    internal static string Key(string fullName, int arity) =>
        arity <= 0 ? fullName : $"{fullName}`{arity}";

    public void Add(AstTypeDefinition definition) {
        _types[Key(definition.FullName, definition.GenericParameters.Count())] = definition;
    }

    public IEnumerable<ITypeDefinition> GetTypeDefinitions() => _types.Values;

    /// <summary>The declarations this registry holds.</summary>
    public IEnumerable<ITypeDefinition> Types => _types.Values;

    /// <summary>The declarations this analysis registered, so a compile can put
    /// them and the root in one unit.</summary>
    public IEnumerable<TypeDefinitionNode> GetTypeDefinitionNodes() =>
        _types.Values.Select(static type => type.Syntax);

    /// <summary>The definition built for exactly <paramref name="node"/>, or null.
    /// Node identity, not name: a domain can hold two definitions with the same
    /// name (module and runtime shapes).</summary>
    public ITypeDefinition? GetTypeDefinition(TypeDefinitionNode node) =>
        _types.Values.FirstOrDefault(type => ReferenceEquals(type.Syntax, node));

    public ITypeDefinition? GetTypeDefinition(string typeName) {
        if (_types.TryGetValue(typeName, out var def))
            return def;

        var arity = TryReadArity(typeName, out var bareName);
        AstTypeDefinition? match = null;
        foreach (var type in _types.Values) {
            if (!string.Equals(type.Name, bareName, StringComparison.Ordinal)
                && !string.Equals(type.FullName, bareName, StringComparison.Ordinal))
                continue;
            if (arity is int required && type.GenericParameters.Count() != required)
                continue;
            if (match is not null)
                return null;
            match = type;
        }
        return match;
    }

    private static int? TryReadArity(string typeName, out string bareName) {
        var tick = typeName.LastIndexOf('`');
        if (tick > 0
            && int.TryParse(typeName.AsSpan(tick + 1), out var parsed)
            && parsed >= 0) {
            bareName = typeName[..tick];
            return parsed;
        }

        bareName = typeName;
        return null;
    }

    /// <summary>A registry of declarations holds no primitives: a composed provider resolves those.</summary>
    public ITypeDefinition? GetTypeDefinition(PrimitiveType primitiveTypeId) => null;
}

/// <summary>Metadata associating a <see cref="TypeDefinitionNode"/> with its
/// resolved <see cref="ITypeDefinition"/>. Set by
/// <see cref="TypeDefinitionNodeAnalyzer"/> during analysis.</summary>
/// <param name="TypeDefinition">The resolved type definition.</param>
public sealed record TypeDefinitionMetadata(ITypeDefinition TypeDefinition) : IAnalysisMetadata;

public static class TypeDefinitionNodeAnalyzerExtensions {
    public static AnalyzerBuilder UseTypeDefinitionNodeAnalyzer(this AnalyzerBuilder builder) {
        builder.AddAnalyzer(new TypeDefinitionNodeAnalyzer());
        return builder;
    }
}

/// <summary>
/// ITypeDefinition implementation backed by a TypeDefinitionNode AST.
/// </summary>
internal sealed class AstTypeDefinition : ITypeDefinition, IClrTypeDefinition {
    private readonly TypeDefinitionNode _node;
    private readonly ITypeDefinitionProvider _provider;
    private readonly IReadOnlyDictionary<string, ITypeDefinition>? _substitutions;
    private static long _nextOpenId;
    private readonly long _openId;
    private readonly object _closureLock = new();
    private readonly Dictionary<string, AstTypeDefinition> _closures = new(StringComparer.Ordinal);
    private readonly Lazy<ITypeDefinition?> _baseType;
    private readonly Lazy<IReadOnlyList<ITypeDefinition>> _interfaces;
    private readonly Lazy<IReadOnlyList<IParameter>> _genericParameters;
    private readonly Lazy<IReadOnlyList<AstConstructorDefinition>> _constructors;
    private readonly Lazy<IReadOnlyList<AstPropertyDefinition>> _declaredProperties;
    private readonly Lazy<IReadOnlyList<AstMethodDefinition>> _declaredMethods;
    private readonly Lazy<IReadOnlyList<AstFieldDefinition>> _declaredFields;
    private readonly Lazy<IReadOnlyList<ITypeProperty>> _properties;
    private readonly Lazy<IReadOnlyList<ITypeMethod>> _methods;
    private readonly Lazy<IReadOnlyList<ITypeField>> _fields;
    private readonly Lazy<IReadOnlyList<ITypeMember>> _members;

    public AstTypeDefinition(TypeDefinitionNode node, ITypeDefinitionProvider provider)
        : this(node, provider, substitutions: null,
            openId: System.Threading.Interlocked.Increment(ref _nextOpenId)) {
    }

    private AstTypeDefinition(
        TypeDefinitionNode node,
        ITypeDefinitionProvider provider,
        IReadOnlyDictionary<string, ITypeDefinition>? substitutions,
        long openId) {
        _node = node;
        _provider = provider;
        _substitutions = substitutions;
        _openId = openId;
        _baseType = new(() => _node.BaseType is null ? null : ResolveType(_node.BaseType));
        _interfaces = new(() => _node.Interfaces?.Select(ResolveType).ToArray() ?? []);
        _genericParameters = new(() => MapParameters(_node.GenericParameters));
        _constructors = new(() => BuildConstructors());
        _declaredProperties = new(() => BuildDeclaredProperties());
        _declaredMethods = new(() => BuildDeclaredMethods());
        _declaredFields = new(() => BuildDeclaredFields());
        _properties = new(() => BuildProperties());
        _methods = new(() => BuildMethods());
        _fields = new(() => BuildFields());
        _members = new(() => [.. Constructors, .. Properties, .. Methods, .. Fields]);
    }

    internal TypeDefinitionNode Syntax => _node;

    /// <summary>Provider this type was resolved against, including sibling AST types.</summary>
    internal ITypeDefinitionProvider ResolutionProvider => _provider;

    /// <summary>
    /// Close this open definition over <paramref name="arguments"/>: a new view with the
    /// same node and its own substitution map and member lazies. Never mutates this
    /// instance. Closed views are resolution products and are never registered. Views
    /// are interned by structural argument identity so two resolutions of the same closed generic
    /// are reference-equal for member matching.
    /// </summary>
    internal AstTypeDefinition Close(IReadOnlyList<ITypeDefinition> arguments) {
        var parameters = _node.GenericParameters;
        if (parameters is null || parameters.Count == 0 || arguments.Count != parameters.Count)
            return this;
        if (_substitutions is not null)
            throw new InvalidOperationException("A closed generic view cannot be closed again.");
        var key = string.Join("|", arguments.Select(ArgumentKey));
        lock (_closureLock) {
            if (_closures.TryGetValue(key, out var cached))
                return cached;
            var substitutions = new Dictionary<string, ITypeDefinition>(StringComparer.Ordinal);
            for (var i = 0; i < parameters.Count; i++)
                substitutions[parameters[i].Name] = arguments[i];
            var closed = new AstTypeDefinition(_node, _provider, substitutions, _openId);
            _closures[key] = closed;
            return closed;
        }
    }

    /// <summary>The argument this closed view substitutes for a bare generic-parameter name, or null.</summary>
    internal ITypeDefinition? TrySubstitute(string name) =>
        _substitutions is not null && _substitutions.TryGetValue(name, out var substituted)
            ? substituted
            : null;

    /// <summary>Structural identity of this definition for closed-view interning.</summary>
    internal string InterningKey {
        get {
            var key = $"ast:{_openId}";
            if (_substitutions is null)
                return key;
            var parameters = _node.GenericParameters;
            if (parameters is null)
                return key;
            var arguments = new List<string>(parameters.Count);
            foreach (var parameter in parameters) {
                arguments.Add(_substitutions.TryGetValue(parameter.Name, out var argument)
                    ? ArgumentKey(argument)
                    : "?");
            }
            return $"{key}<{string.Join("|", arguments)}>";
        }
    }

    private static string ArgumentKey(ITypeDefinition argument) => argument switch {
        AstTypeDefinition ast => ast.InterningKey,
        AstCollectionTypeDefinition collection => $"collection:{collection.Kind}:{ArgumentKey(collection.ElementType)}",
        AstGenericParameterTypeDefinition parameter => $"parameter:{parameter.Name}@{DeclaringKey(parameter.DeclaringType)}",
        _ => $"clr:{argument.GetRuntimeTypeOrThrow()}"
    };

    private static string DeclaringKey(ITypeDefinition declaring) =>
        declaring is AstTypeDefinition ast
            ? ast.InterningKey
            : $"clr:{declaring.GetRuntimeTypeOrThrow()}";

    public string Name => _node.Name;
    public string? Namespace => _node.Namespace;
    public string FullName => _node.FullName;
    public AccessModifier AccessModifier => _node.AccessModifier;

    public IEnumerable<ITypeMember> Members => _members.Value;

    public IEnumerable<ITypeField> Fields => _fields.Value;
    public IEnumerable<ITypeProperty> Properties => _properties.Value;
    public IEnumerable<ITypeMethod> Methods => _methods.Value;
    public IEnumerable<ITypeConstructor> Constructors => _constructors.Value;

    /// <summary>
    /// The declared static methods marked as an operator (<see cref="MethodDefinitionNode.Operator"/>) whose parameter
    /// count is the operator's arity. A method's name never makes it an operator.
    /// </summary>
    public IEnumerable<ITypeOperator> Operators => _declaredMethods.Value
        .Where(static method => method.IsStatic
            && method.DefinitionNode.Operator is { } kind
            && method.Parameters.Count() == OperatorShape.ArityOf(kind))
        .Select(static method => new AstOperatorDefinition(method.DefinitionNode.Operator!.Value, method));

    ITypeOperator? ITypeDefinition.ResolveOperator(OperatorKind kind, ITypeDefinition? right) => ClrOperatorResolution.Resolve(this, kind, right);

    /// <summary>
    /// The conversions this type states: its static methods marked <see cref="MethodDefinitionNode.Conversion"/> with
    /// one parameter, either end. A method's name never makes it a conversion, and a modeled definition has no
    /// platform-applied conversions, so there is nothing else to state.
    /// </summary>
    IEnumerable<ITypeConversion> ITypeDefinition.Conversions => _declaredMethods.Value
        .Where(static method => method.IsStatic
            && method.DefinitionNode.Conversion is { } kind
            && method.Parameters.Count() == 1)
        .Select(static method => (ITypeConversion)new AstConversionDefinition(
            method.DefinitionNode.Conversion!.Value, method));

    ITypeConversion? ITypeDefinition.ResolveConversionFrom(ITypeDefinition source) => ModeledConversions.Find(source, this);

    bool ITypeDefinition.IsCollectionShape => false;
    ITypeDefinition? ITypeDefinition.SequenceElementType => null;

    /// <summary>A modeled definition is its instance: each declaration builds one, and a closed generic is a closure
    /// of it, a distinct definition.</summary>
    bool ITypeDefinition.IsSameTypeAs(ITypeDefinition other) => ReferenceEquals(this, other);

    // AST-based types are dictionary-backed at runtime
    public Type RuntimeType => typeof(IDictionary<string, object>);

    public ITypeDefinition? BaseType => _baseType.Value;
    public IEnumerable<ITypeDefinition> Interfaces => _interfaces.Value;
    public IEnumerable<IParameter> GenericParameters => _genericParameters.Value;

    public PrimitiveType? PrimitiveType => _node.PrimitiveTypeId;

    /// <summary>A modeled primitive the shared vocabulary does not name is stated by its declaration's name.</summary>
    PrimitiveAtom? ITypeDefinition.PlatformAtom =>
        _node.PrimitiveTypeId is null && _node.EffectiveKind == TypeKind.Primitive
            ? new PrimitiveAtom(Name)
            : null;

    /// <summary>A modeled definition states no nullable form; optionality is the caller's (T-06).</summary>
    ITypeDefinition? ITypeDefinition.NullableOf => null;
    TypeKind ITypeDefinition.Kind => _node.EffectiveKind;

    private List<AstPropertyDefinition> BuildDeclaredProperties() {
        var properties = new List<PropertyDefinitionNode>();

        if (_node.Properties is not null) {
            properties.AddRange(_node.Properties);
        }

        if (_node.PrimaryConstructorParameters is not null) {
            var explicitPropertyNames = new HashSet<string>(properties.Select(static property => property.Name), StringComparer.Ordinal);
            foreach (var parameter in _node.PrimaryConstructorParameters) {
                if (parameter.TypeReference is null || !explicitPropertyNames.Add(parameter.Name)) {
                    continue;
                }

                properties.Add(new PropertyDefinitionNode(
                    parameter.Name,
                    parameter.TypeReference,
                    Getter: new PropertyGetterDefinitionNode()));
            }
        }

        return [.. properties.Select(p => new AstPropertyDefinition(p, this))];
    }

    private List<AstConstructorDefinition> BuildConstructors() {
        var constructors = new List<ConstructorDefinitionNode>();

        if (_node.PrimaryConstructorParameters is { Count: > 0 }) {
            constructors.Add(new ConstructorDefinitionNode(_node.PrimaryConstructorParameters));
        }

        if (_node.Constructors is not null) {
            constructors.AddRange(_node.Constructors);
        }

        return [.. constructors.Select(constructor => new AstConstructorDefinition(constructor, this))];
    }

    private List<AstMethodDefinition> BuildDeclaredMethods() {
        return _node.Methods?
            .Select(m => new AstMethodDefinition(m, this))
            .ToList() ?? [];
    }

    private List<AstFieldDefinition> BuildDeclaredFields() {
        return _node.Fields?
            .Select(f => new AstFieldDefinition(f, this))
            .ToList() ?? [];
    }

    private List<ITypeProperty> BuildProperties() {
        return ComposeInheritedMembers(
            _declaredProperties.Value,
            static typeDefinition => typeDefinition.Properties,
            static property => $"{property.Name}|{property.LifetimeModifier}|{GetParameterSignature(property.Parameters)}");
    }

    private List<ITypeMethod> BuildMethods() {
        return ComposeInheritedMembers(
            _declaredMethods.Value,
            static typeDefinition => typeDefinition.Methods,
            static method => $"{method.Name}|{method.LifetimeModifier}|{GetParameterSignature(method.Parameters)}",
            static method => method.AccessModifier == AccessModifier.Public && !method.IsStatic);
    }

    private List<ITypeField> BuildFields() {
        return ComposeInheritedMembers(
            _declaredFields.Value,
            static typeDefinition => typeDefinition.Fields,
            static field => $"{field.Name}|{field.LifetimeModifier}");
    }

    private List<TMember> ComposeInheritedMembers<TMember>(
        IEnumerable<TMember> declaredMembers,
        Func<ITypeDefinition, IEnumerable<TMember>> inheritedMemberSelector,
        Func<TMember, string> keySelector,
        Func<TMember, bool>? inheritedFilter = null) {
        ArgumentNullException.ThrowIfNull(declaredMembers);
        ArgumentNullException.ThrowIfNull(inheritedMemberSelector);
        ArgumentNullException.ThrowIfNull(keySelector);

        var members = new List<TMember>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        AddMembers(declaredMembers);

        if (BaseType is not null) {
            AddMembers(inheritedMemberSelector(BaseType));
        }
        else {
            // Every C# type with no declared base derives from System.Object; its
            // members (ToString, Equals, GetHashCode, GetType) are available on an
            // AST type too. Declared members already added above still win by key.
            var objectType = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(object));
            if (objectType is not null) {
                var inherited = inheritedMemberSelector(objectType);
                if (inheritedFilter is not null)
                    inherited = inherited.Where(inheritedFilter);
                AddMembers(inherited);
            }
        }

        foreach (var implementedInterface in Interfaces) {
            AddMembers(inheritedMemberSelector(implementedInterface));
        }

        return members;

        void AddMembers(IEnumerable<TMember> source) {
            foreach (var member in source) {
                if (seen.Add(keySelector(member))) {
                    members.Add(member);
                }
            }
        }
    }

    private static string GetParameterSignature(IEnumerable<IParameter> parameters) {
        return string.Join(",", parameters.Select(static parameter => parameter.ParameterTypeDefinition.FullName));
    }

    internal ITypeDefinition ResolveType(Node typeNode) =>
        AstTypeReferenceResolver.Resolve(typeNode, _provider, this);

    internal IReadOnlyList<IParameter> MapParameters(IReadOnlyList<Parameter>? parameters) {
        return parameters?
            .Select((parameter, index) => new AstParameterDefinition(parameter, index, this))
            .Cast<IParameter>()
            .ToArray() ?? [];
    }
}

internal sealed class AstConstructorDefinition : ITypeConstructor {
    private readonly ConstructorDefinitionNode _node;
    private readonly AstTypeDefinition _declaringType;
    private readonly Lazy<IReadOnlyList<IParameter>> _parameters;

    public AstConstructorDefinition(ConstructorDefinitionNode node, AstTypeDefinition declaringType) {
        _node = node;
        _declaringType = declaringType;
        _parameters = new(() => _declaringType.MapParameters(_node.Parameters));
    }

    internal ConstructorDefinitionNode Definition => _node;

    public string Name => _declaringType.Name;
    public ITypeDefinition MemberTypeDefinition => _declaringType;
    public ITypeDefinition DeclaringTypeDefinition => _declaringType;
    public MemberKind Kind => MemberKind.Constructor;
    public IEnumerable<IParameter> Parameters => _parameters.Value;
    public AccessModifier AccessModifier => _node.AccessModifier;
    public LifetimeModifier LifetimeModifier => LifetimeModifier.Instance;

    public Mutability Mutability => Mutability.Mutable;
}

internal sealed class AstParameterDefinition : IParameter {
    private readonly Parameter _node;
    private readonly AstTypeDefinition _declaringType;
    private readonly Lazy<ITypeDefinition> _parameterType;
    private readonly Lazy<object?> _defaultValue;

    public AstParameterDefinition(Parameter node, int position, AstTypeDefinition declaringType) {
        _node = node;
        _declaringType = declaringType;
        Position = position;
        _parameterType = new(() => _node.TypeReference is null
            ? ClrTypeDefinitionRegistry.Shared.GetTypeDefinition<object>()
            : _declaringType.ResolveType(_node.TypeReference));
        _defaultValue = new(() => _node.DefaultValue is Constant constant ? constant.Value : null);
    }

    public int Position { get; }
    public string Name => _node.Name;
    public ITypeDefinition ParameterTypeDefinition => _parameterType.Value;
    public bool IsOptional => _node.DefaultValue is not null;
    public object? DefaultValue => _defaultValue.Value;
}

/// <summary>
/// ITypeProperty implementation backed by a PropertyDefinitionNode AST.
/// At runtime the declaring type is dictionary-backed (IDictionary&lt;string, object&gt;),
/// so the Read delegate indexes into the dictionary by property name.
/// </summary>
internal sealed class AstPropertyDefinition(PropertyDefinitionNode node, AstTypeDefinition declaring) : ITypeProperty {
    private readonly PropertyDefinitionNode _node = node;
    private readonly AstTypeDefinition _declaring = declaring;
    private readonly Lazy<ITypeDefinition> _memberType = new(() => declaring.ResolveType(node.MemberType));

    public string Name => _node.Name;
    public ITypeDefinition MemberTypeDefinition => _memberType.Value;
    public ITypeDefinition DeclaringTypeDefinition => _declaring;

    internal PropertyDefinitionNode Definition => _node;

    public MemberKind Kind => _node.IndexParameters is { Count: > 0 } ? MemberKind.Indexer : MemberKind.Property;

    internal ITypeDefinition? TryGetCollectionElementType() =>
        _node.MemberType is CollectionTypeReference col
            ? _declaring.ResolveType(col.ElementType)
            : null;

    public IEnumerable<IParameter> Parameters => _node.IndexParameters is null ? [] : _declaring.MapParameters(_node.IndexParameters);

    public AccessModifier AccessModifier => _node.AccessModifier;
    public LifetimeModifier LifetimeModifier => _node.IsStatic ? LifetimeModifier.Static : LifetimeModifier.Instance;
    public bool IsStatic => _node.IsStatic;

    public Mutability Mutability => _node.Mutability;
}

/// <summary>
/// ITypeMethod implementation backed by a MethodDefinitionNode AST.
/// </summary>
internal sealed class AstMethodDefinition(MethodDefinitionNode node, AstTypeDefinition declaring) : ITypeMethod {
    private readonly MethodDefinitionNode _node = node;
    private readonly AstTypeDefinition _declaring = declaring;
    private readonly Lazy<ITypeDefinition> _returnType = new(() => declaring.ResolveType(node.ReturnType));

    public MethodDefinitionNode DefinitionNode => _node;
    public string Name => _node.Name;
    public ITypeDefinition MemberTypeDefinition => _returnType.Value;
    public ITypeDefinition DeclaringTypeDefinition => _declaring;
    public MemberKind Kind => MemberKind.Method;
    public IEnumerable<IParameter> Parameters => _declaring.MapParameters(_node.Parameters);
    public AccessModifier AccessModifier => _node.AccessModifier;
    public LifetimeModifier LifetimeModifier => _node.IsStatic ? LifetimeModifier.Static : LifetimeModifier.Instance;
    public bool IsStatic => _node.IsStatic;

    public Mutability Mutability => Mutability.Mutable;
}

/// <summary>
/// ITypeOperator implementation backed by a MethodDefinitionNode AST marked as an operator.
/// </summary>
internal sealed class AstOperatorDefinition(OperatorKind kind, AstMethodDefinition method) : ITypeOperator {
    public OperatorKind Kind => kind;
    public ITypeDefinition DeclaringTypeDefinition => method.DeclaringTypeDefinition;
    public IEnumerable<IParameter> Parameters => method.Parameters;
    public ITypeDefinition ResultTypeDefinition => method.MemberTypeDefinition;
    public ITypeMethod? Method => method;
}

/// <summary>
/// ITypeConversion implementation backed by a MethodDefinitionNode AST marked as a conversion.
/// </summary>
internal sealed class AstConversionDefinition(ConversionOperatorKind kind, AstMethodDefinition method) : ITypeConversion {
    public ConversionOperatorKind Kind => kind;
    public ITypeDefinition SourceTypeDefinition => method.Parameters.Single().ParameterTypeDefinition;
    public ITypeDefinition ResultTypeDefinition => method.MemberTypeDefinition;
    public ITypeMethod? Method => method;
}

/// <summary>
/// ITypeField implementation backed by a FieldDefinitionNode AST.
/// </summary>
internal sealed class AstFieldDefinition(FieldDefinitionNode node, AstTypeDefinition declaring) : ITypeField {
    private readonly FieldDefinitionNode _node = node;
    private readonly AstTypeDefinition _declaring = declaring;
    private readonly Lazy<ITypeDefinition> _fieldType = new(() => declaring.ResolveType(node.FieldType));

    public string Name => _node.Name;
    public ITypeDefinition MemberTypeDefinition => _fieldType.Value;
    public ITypeDefinition DeclaringTypeDefinition => _declaring;
    public IEnumerable<IParameter> Parameters => [];

    internal FieldDefinitionNode Definition => _node;

    public MemberKind Kind => MemberKind.Field;

    public AccessModifier AccessModifier => _node.AccessModifier;
    public LifetimeModifier LifetimeModifier => _node.IsStatic ? LifetimeModifier.Static : LifetimeModifier.Instance;
    public bool IsStatic => _node.IsStatic;

    public Mutability Mutability => _node.Mutability;
}

/// <summary>
/// Utility class to resolve AST type reference nodes to ITypeDefinition.
/// </summary>
internal static class AstTypeReferenceResolver {
    public static ITypeDefinition Resolve(
        Node typeNode,
        ITypeDefinitionProvider provider,
        ITypeDefinition? enclosing = null) {
        return TryResolve(typeNode, provider, enclosing)
            ?? throw new InvalidOperationException(
                $"Type with name '{TypeNameOf(typeNode)}' not found.");
    }

    public static ITypeDefinition? TryResolve(
        Node typeNode,
        ITypeDefinitionProvider provider,
        ITypeDefinition? enclosing = null) {
        var clr = ClrTypeDefinitionRegistry.Shared;

        return typeNode switch {
            PrimitiveTypeReference prim => ResolvePrimitive(prim.PrimitiveId, prim.IsNullable, clr),
            NamedTypeReference named => ResolveNamed(named, provider, clr, enclosing),
            OptionalTypeReference opt => ResolveOptional(opt, provider, clr, enclosing),
            CollectionTypeReference col => ResolveCollection(col, provider, clr, enclosing),
            MapTypeReference map => ResolveMap(map, provider, clr, enclosing),
            UnionTypeReference union => ResolveUnion(union, provider, clr, enclosing),
            TypeDefinitionReference tdr => tdr.TypeDefinition,
            TypeReference tr => ResolveByName(tr.TypeName, provider, clr),
            _ => clr.GetTypeDefinition<object>()
        };
    }

    private static string TypeNameOf(Node typeNode) => typeNode switch {
        NamedTypeReference named => named.FullName,
        TypeReference tr => tr.TypeName,
        _ => typeNode.ToString() ?? typeNode.GetType().Name
    };

    private static ClrTypeDefinition ResolvePrimitive(PrimitiveType id, bool isNullable, ClrTypeDefinitionRegistry clr) {
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
            PrimitiveType.NInt => clr.GetTypeDefinition<nint>(),
            PrimitiveType.NUInt => clr.GetTypeDefinition<nuint>(),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "no CLR definition for this primitive")
        };

        if (isNullable && baseType.RuntimeType.IsValueType) {
            var nullableType = typeof(Nullable<>).MakeGenericType(baseType.RuntimeType);
            return clr.GetTypeDefinition(nullableType);
        }

        return baseType;
    }

    private static ITypeDefinition? ResolveNamed(
        NamedTypeReference named,
        ITypeDefinitionProvider provider,
        ClrTypeDefinitionRegistry clr,
        ITypeDefinition? enclosing) {
        if (named.TypeArguments is { Count: > 0 } args) {
            var resolvedArgs = new ITypeDefinition[args.Count];
            ITypeDefinition? open = null;
            for (var i = 0; i < args.Count; i++) {
                var resolved = TryResolve(args[i], provider, enclosing);
                if (resolved is null && !TryCollectionKind(named.TypeName, out _)) {
                    // DomainResult<T> inside the open definition: T is that definition's parameter,
                    // not a separate type. An unknown name still fails.
                    open ??= LookupOpenGeneric(named, args.Count, provider);
                    resolved = open is null ? null : TypeParameterOf(open, args[i]);
                }
                if (resolved is null)
                    return null;
                resolvedArgs[i] = resolved;
            }
            return CloseNamed(named, resolvedArgs, provider, clr, enclosing);
        }

        return LookupNamed(named, provider, clr, enclosing, allowTypeParameter: true);
    }

    private static ITypeDefinition? LookupNamed(
        NamedTypeReference named,
        ITypeDefinitionProvider provider,
        ClrTypeDefinitionRegistry clr,
        ITypeDefinition? enclosing,
        bool allowTypeParameter) {
        if (named.Namespace is null
            && string.Equals(named.TypeName, "void", StringComparison.OrdinalIgnoreCase))
            return clr.GetTypeDefinition(typeof(void));

        if (allowTypeParameter && enclosing is not null && named.Namespace is null) {
            // A closed view's own argument overrides the open parameter scan:
            // `T` inside `Box<Widget>` is `Widget`, not the open `T`.
            if (enclosing is AstTypeDefinition closed
                && closed.TrySubstitute(named.TypeName) is { } substituted)
                return substituted;
            foreach (var genericParameter in enclosing.GenericParameters) {
                if (string.Equals(genericParameter.Name, named.TypeName, StringComparison.Ordinal))
                    return new AstGenericParameterTypeDefinition(genericParameter, enclosing);
            }
        }

        return provider.GetTypeDefinition(named.FullName)
            ?? (named.Namespace is not null ? provider.GetTypeDefinition(named.TypeName) : null)
            ?? clr.GetTypeDefinition(named.FullName)
            ?? (named.FullName != named.TypeName ? clr.GetTypeDefinition(named.TypeName) : null);
    }

    private static ITypeDefinition? CloseNamed(
        NamedTypeReference named,
        ITypeDefinition[] args,
        ITypeDefinitionProvider provider,
        ClrTypeDefinitionRegistry clr,
        ITypeDefinition? enclosing) {
        if (args.Length == 1 && TryCollectionKind(named.TypeName, out var kind))
            return CloseCollection(args[0], kind, clr);

        // A provider that declares an arity key as an already-constructed CLR generic
        // (the VM's DomainResult`1 -> DomainResult<object>) is the answer itself: closing
        // it again over the argument types would rebuild a different closed type.
        if (LookupConstructedGeneric(named, args.Length, provider) is { } constructed)
            return constructed;

        var open = LookupOpenGeneric(named, args.Length, provider)
            ?? LookupNamed(named, provider, clr, enclosing, allowTypeParameter: false);
        if (open is null)
            return null;
        return CloseGeneric(open, args, clr);
    }

    private static ITypeDefinition? TypeParameterOf(ITypeDefinition open, Node argument) {
        if (argument is not NamedTypeReference named || named.Namespace is not null || named.TypeArguments is { Count: > 0 })
            return null;
        foreach (var parameter in open.GenericParameters) {
            if (string.Equals(parameter.Name, named.TypeName, StringComparison.Ordinal))
                return new AstGenericParameterTypeDefinition(parameter, open);
        }
        return null;
    }

    private static ITypeDefinition? LookupConstructedGeneric(
        NamedTypeReference named,
        int arity,
        ITypeDefinitionProvider provider) {
        if (arity <= 0)
            return null;

        if (AsConstructedClr(provider.GetTypeDefinition(AstTypeRegistry.Key(named.FullName, arity))) is { } full)
            return full;
        return named.FullName == named.TypeName
            ? null
            : AsConstructedClr(provider.GetTypeDefinition(AstTypeRegistry.Key(named.TypeName, arity)));
    }

    private static ITypeDefinition? AsConstructedClr(ITypeDefinition? definition) =>
        // Only a CLR definition is accepted: an AST definition also reports a constructed RuntimeType
        // (IDictionary<,>), but it closes through its own substitution map.
        definition is ClrTypeDefinition { RuntimeType: { IsConstructedGenericType: true, ContainsGenericParameters: false } }
            ? definition
            : null;

    private static ITypeDefinition? LookupOpenGeneric(
        NamedTypeReference named,
        int arity,
        ITypeDefinitionProvider provider) {
        if (arity <= 0)
            return null;

        // Arity keys are an AST-registry fact here. A CLR hit on `Name`1` is accepted only
        // through LookupConstructedGeneric, so it cannot steal the call from a non-generic
        // runtime type or be closed a second time.
        var fullKey = AstTypeRegistry.Key(named.FullName, arity);
        if (provider.GetTypeDefinition(fullKey) is AstTypeDefinition full)
            return full;
        if (named.FullName == named.TypeName)
            return null;

        var shortKey = AstTypeRegistry.Key(named.TypeName, arity);
        return provider.GetTypeDefinition(shortKey) as AstTypeDefinition;
    }

    private static bool TryCollectionKind(string typeName, out CollectionKind kind) {
        switch (typeName) {
            case "List":
            case "IList":
            case "ICollection":
            case "IEnumerable":
            case "IReadOnlyList":
            case "IReadOnlyCollection":
                kind = CollectionKind.List;
                return true;
            case "HashSet":
            case "ISet":
                kind = CollectionKind.Set;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static ITypeDefinition CloseCollection(
        ITypeDefinition element,
        CollectionKind kind,
        ClrTypeDefinitionRegistry clr) {
        if (element is AstTypeDefinition or AstCollectionTypeDefinition or AstGenericParameterTypeDefinition)
            return new AstCollectionTypeDefinition(element, kind);

        var elementClrType = element.GetRuntimeTypeOrThrow();
        var collectionClrType = kind switch {
            CollectionKind.Array => elementClrType.MakeArrayType(),
            CollectionKind.List => typeof(List<>).MakeGenericType(elementClrType),
            CollectionKind.Set => typeof(HashSet<>).MakeGenericType(elementClrType),
            _ => typeof(IEnumerable<>).MakeGenericType(elementClrType)
        };
        return clr.GetTypeDefinition(collectionClrType);
    }

    private static ITypeDefinition CloseGeneric(
        ITypeDefinition open,
        ITypeDefinition[] args,
        ClrTypeDefinitionRegistry clr) {
        // An AST generic closes through its own substitution map; the CLR branch below
        // would otherwise close it through its RuntimeType (IDictionary<,> for 2-arity).
        if (open is AstTypeDefinition astOpen)
            return astOpen.Close(args);
        if (open is not IClrTypeDefinition clrOpen)
            return open;

        var runtimeType = clrOpen.RuntimeType;
        var definition = runtimeType.IsGenericTypeDefinition
            ? runtimeType
            : runtimeType.IsGenericType ? runtimeType.GetGenericTypeDefinition() : null;
        if (definition is null || definition.GetGenericArguments().Length != args.Length)
            return open;

        var clrArgs = new Type[args.Length];
        for (var i = 0; i < args.Length; i++)
            clrArgs[i] = args[i].GetRuntimeType() ?? typeof(object);
        return clr.GetTypeDefinition(definition.MakeGenericType(clrArgs));
    }

    private static ITypeDefinition ResolveOptional(
        OptionalTypeReference opt,
        ITypeDefinitionProvider provider,
        ClrTypeDefinitionRegistry clr,
        ITypeDefinition? enclosing) {
        var innerType = Resolve(opt.InnerType, provider, enclosing);
        var innerClrType = innerType.GetRuntimeTypeOrThrow();

        if (!innerClrType.IsValueType || Nullable.GetUnderlyingType(innerClrType) != null)
            return innerType;

        var nullableType = typeof(Nullable<>).MakeGenericType(innerClrType);
        return clr.GetTypeDefinition(nullableType);
    }

    private static ITypeDefinition? ResolveByName(
        string name, ITypeDefinitionProvider provider, ClrTypeDefinitionRegistry clr) {
        if (string.Equals(name, "void", StringComparison.OrdinalIgnoreCase))
            return clr.GetTypeDefinition(typeof(void));
        return provider.GetTypeDefinition(name) ?? clr.GetTypeDefinition(name);
    }

    private static ITypeDefinition ResolveCollection(
        CollectionTypeReference col,
        ITypeDefinitionProvider provider,
        ClrTypeDefinitionRegistry clr,
        ITypeDefinition? enclosing) {
        var elementType = Resolve(col.ElementType, provider, enclosing);
        return CloseCollection(elementType, col.Kind, clr);
    }

    private static ClrTypeDefinition ResolveMap(
        MapTypeReference map,
        ITypeDefinitionProvider provider,
        ClrTypeDefinitionRegistry clr,
        ITypeDefinition? enclosing) {
        var keyType = Resolve(map.KeyType, provider, enclosing);
        var valueType = Resolve(map.ValueType, provider, enclosing);

        var dictType = typeof(Dictionary<,>).MakeGenericType(
            keyType.GetRuntimeTypeOrThrow(),
            valueType.GetRuntimeTypeOrThrow()
        );
        return clr.GetTypeDefinition(dictType);
    }

    private static ITypeDefinition ResolveUnion(
        UnionTypeReference union,
        ITypeDefinitionProvider provider,
        ClrTypeDefinitionRegistry clr,
        ITypeDefinition? enclosing) {
        if (union.Options.Count == 0) {
            return clr.GetTypeDefinition<object>();
        }

        var optionTypes = union.Options.Select(option => Resolve(option, provider, enclosing)).ToArray();
        var firstRuntimeType = optionTypes[0].GetRuntimeTypeOrThrow();

        // Preserve precision only when all options collapse to the same CLR runtime type.
        var allSameRuntimeType = optionTypes.All(type => type.GetRuntimeTypeOrThrow() == firstRuntimeType);
        return allSameRuntimeType ? optionTypes[0] : clr.GetTypeDefinition<object>();
    }
}

internal sealed class AstCollectionTypeDefinition : ITypeDefinition, IClrTypeDefinition {
    private readonly AstCollectionItemProperty _item;
    private readonly ITypeProperty? _count;
    private readonly ITypeConstructor _ctor;
    private readonly ITypeConstructor _copy;
    private readonly ITypeMethod _add;

    public AstCollectionTypeDefinition(ITypeDefinition element, CollectionKind kind) {
        ElementType = element;
        Kind = kind;
        _item = new AstCollectionItemProperty(this, element);
        _count = ClrTypeDefinitionRegistry.Shared
            .GetTypeDefinition(typeof(System.Collections.IList))
            ?.Properties.FirstOrDefault(p => string.Equals(p.Name, "Count", StringComparison.Ordinal));
        _ctor = new AstCollectionParameterlessConstructor(this);
        // new List<T>(items ?? new List<T>()) on a printed entity constructor.
        _copy = new AstCollectionCopyConstructor(this);
        // DomainResult.Value is object. Add still has to accept the entity.
        var addArg = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition<object>();
        _add = new AstCollectionAddMethod(this, addArg);
    }

    public ITypeDefinition ElementType { get; }
    public CollectionKind Kind { get; }
    public string Name => Kind == CollectionKind.Array ? $"{ElementType.Name}[]" : $"List<{ElementType.Name}>";
    public string? Namespace => null;
    public AccessModifier AccessModifier => AccessModifier.Public;
    public ITypeDefinition? BaseType => null;
    public IEnumerable<ITypeDefinition> Interfaces => [];
    public IEnumerable<IParameter> GenericParameters => [];
    public IEnumerable<ITypeMember> Members => _count is null
        ? [_item, _ctor, _copy, _add]
        : [_item, _count, _ctor, _copy, _add];
    public IEnumerable<ITypeField> Fields => [];
    public IEnumerable<ITypeProperty> Properties => _count is null ? [_item] : [_item, _count];
    public IEnumerable<ITypeMethod> Methods => [_add];
    public IEnumerable<ITypeConstructor> Constructors => [_ctor, _copy];
    public IEnumerable<ITypeOperator> Operators => [];
    IEnumerable<ITypeConversion> ITypeDefinition.Conversions => [];
    ITypeOperator? ITypeDefinition.ResolveOperator(OperatorKind kind, ITypeDefinition? right) => ClrOperatorResolution.Resolve(this, kind, right);
    ITypeConversion? ITypeDefinition.ResolveConversionFrom(ITypeDefinition source) => ModeledConversions.Find(source, this);
    bool ITypeDefinition.IsCollectionShape => true;
    ITypeDefinition? ITypeDefinition.SequenceElementType => _item.MemberTypeDefinition;

    /// <summary>
    /// Two synthetic collections of one kind and element are the same type: each resolve builds a fresh instance, so
    /// reference equality alone never matches an omitted collection argument. A nested collection compares by this
    /// notion again (its name does not carry its element's namespace).
    /// </summary>
    bool ITypeDefinition.IsSameTypeAs(ITypeDefinition other) {
        if (ReferenceEquals(this, other))
            return true;
        if (other is not AstCollectionTypeDefinition right || Kind != right.Kind)
            return false;
        if (ElementType is AstCollectionTypeDefinition || right.ElementType is AstCollectionTypeDefinition)
            return TypeDefinitionExtensions.IsSameType(ElementType, right.ElementType);
        return string.Equals(ElementType.Name, right.ElementType.Name, StringComparison.Ordinal)
            && string.Equals(ElementType.Namespace, right.ElementType.Namespace, StringComparison.Ordinal);
    }
    public PrimitiveType? PrimitiveType => null;
    PrimitiveAtom? ITypeDefinition.PlatformAtom => null;
    ITypeDefinition? ITypeDefinition.NullableOf => null;
    TypeKind ITypeDefinition.Kind => TypeKind.Class;
    public Type RuntimeType => typeof(System.Collections.IList);
}

/// <summary>Parameterless constructor for a synthetic collection type.</summary>
internal sealed class AstCollectionParameterlessConstructor(AstCollectionTypeDefinition declaring) : ITypeConstructor {
    public string Name => declaring.Name;
    public ITypeDefinition MemberTypeDefinition => declaring;
    public ITypeDefinition DeclaringTypeDefinition => declaring;
    public MemberKind Kind => MemberKind.Constructor;
    public IEnumerable<IParameter> Parameters => [];
    public AccessModifier AccessModifier => AccessModifier.Public;
    public LifetimeModifier LifetimeModifier => LifetimeModifier.Instance;
    public Mutability Mutability => Mutability.Mutable;
}

/// <summary><c>new List&lt;T&gt;(items)</c> for a synthetic collection type.</summary>
internal sealed class AstCollectionCopyConstructor(AstCollectionTypeDefinition declaring) : ITypeConstructor {
    public string Name => declaring.Name;
    public ITypeDefinition MemberTypeDefinition => declaring;
    public ITypeDefinition DeclaringTypeDefinition => declaring;
    public MemberKind Kind => MemberKind.Constructor;
    public IEnumerable<IParameter> Parameters => [new AstCollectionSourceParameter(declaring)];
    public AccessModifier AccessModifier => AccessModifier.Public;
    public LifetimeModifier LifetimeModifier => LifetimeModifier.Instance;
    public Mutability Mutability => Mutability.Mutable;
}

/// <summary>The source sequence of <c>new List&lt;T&gt;(items)</c>.</summary>
internal sealed class AstCollectionSourceParameter(AstCollectionTypeDefinition source) : IParameter {
    public int Position => 0;
    public string Name => "collection";
    public ITypeDefinition ParameterTypeDefinition => source;
    public bool IsOptional => false;
    public object? DefaultValue => null;
}

/// <summary><c>Add(X)</c> on a synthetic collection type.</summary>
internal sealed class AstCollectionAddMethod(AstCollectionTypeDefinition declaring, ITypeDefinition element) : ITypeMethod {
    public string Name => "Add";
    public ITypeDefinition MemberTypeDefinition => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(void))!;
    public ITypeDefinition DeclaringTypeDefinition => declaring;
    public MemberKind Kind => MemberKind.Method;
    public IEnumerable<IParameter> Parameters => [new AstCollectionElementParameter(element)];
    public AccessModifier AccessModifier => AccessModifier.Public;
    public LifetimeModifier LifetimeModifier => LifetimeModifier.Instance;
    public bool IsStatic => false;
    public Mutability Mutability => Mutability.Mutable;
}

/// <summary>The element parameter of <c>Add(X)</c>.</summary>
internal sealed class AstCollectionElementParameter(ITypeDefinition element) : IParameter {
    public int Position => 0;
    public string Name => "item";
    public ITypeDefinition ParameterTypeDefinition => element;
    public bool IsOptional => false;
    public object? DefaultValue => null;
}

internal sealed class AstGenericParameterTypeDefinition : ITypeDefinition, IClrTypeDefinition {
    public AstGenericParameterTypeDefinition(IParameter parameter, ITypeDefinition declaringType) {
        Name = parameter.Name;
        DeclaringType = declaringType;
    }

    public string Name { get; }
    public ITypeDefinition DeclaringType { get; }
    public string? Namespace => null;
    public AccessModifier AccessModifier => AccessModifier.Public;
    public ITypeDefinition? BaseType => null;
    public IEnumerable<ITypeDefinition> Interfaces => [];
    public IEnumerable<IParameter> GenericParameters => [];
    public IEnumerable<ITypeMember> Members => [];
    public IEnumerable<ITypeField> Fields => [];
    public IEnumerable<ITypeProperty> Properties => [];
    public IEnumerable<ITypeMethod> Methods => [];
    public IEnumerable<ITypeConstructor> Constructors => [];
    public IEnumerable<ITypeOperator> Operators => [];
    IEnumerable<ITypeConversion> ITypeDefinition.Conversions => [];
    ITypeOperator? ITypeDefinition.ResolveOperator(OperatorKind kind, ITypeDefinition? right) => ClrOperatorResolution.Resolve(this, kind, right);
    ITypeConversion? ITypeDefinition.ResolveConversionFrom(ITypeDefinition source) => ModeledConversions.Find(source, this);
    bool ITypeDefinition.IsCollectionShape => false;
    ITypeDefinition? ITypeDefinition.SequenceElementType => null;

    /// <summary>
    /// A generic parameter is its name on its declaring type. The definition holds no declaring member, so two
    /// method-level parameters of one name on one type are the same type here: a stated limitation, and no DSL
    /// construct declares a method-level type parameter.
    /// </summary>
    bool ITypeDefinition.IsSameTypeAs(ITypeDefinition other) =>
        ReferenceEquals(this, other)
        || (other is AstGenericParameterTypeDefinition right
            && string.Equals(Name, right.Name, StringComparison.Ordinal)
            && TypeDefinitionExtensions.IsSameType(DeclaringType, right.DeclaringType));

    public PrimitiveType? PrimitiveType => null;
    PrimitiveAtom? ITypeDefinition.PlatformAtom => null;
    ITypeDefinition? ITypeDefinition.NullableOf => null;
    TypeKind ITypeDefinition.Kind => TypeKind.Class;
    public Type RuntimeType => typeof(object);
}

internal sealed class AstCollectionItemProperty : ITypeProperty {
    public AstCollectionItemProperty(ITypeDefinition declaring, ITypeDefinition element) {
        DeclaringTypeDefinition = declaring;
        MemberTypeDefinition = element;
        Parameters = [
            new AstIndexParameter(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition<int>())
        ];
    }

    public string Name => "Item";
    public ITypeDefinition MemberTypeDefinition { get; }
    public ITypeDefinition DeclaringTypeDefinition { get; }
    public MemberKind Kind => MemberKind.Indexer;
    public IEnumerable<IParameter> Parameters { get; }
    public AccessModifier AccessModifier => AccessModifier.Public;
    public LifetimeModifier LifetimeModifier => LifetimeModifier.Instance;
    public Mutability Mutability => Mutability.Mutable;
}

internal sealed class AstIndexParameter(ITypeDefinition type) : IParameter {
    public int Position => 0;
    public string Name => "index";
    public ITypeDefinition ParameterTypeDefinition { get; } = type;
    public bool IsOptional => false;
    public object? DefaultValue => null;
}