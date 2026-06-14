using System.Reflection;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// Reflection-backed constructor member for a CLR type.
/// Exposes the constructed type, declaring type, and ordered constructor parameters.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
internal sealed class ClrConstructor : ClrTypeMember, ITypeConstructor {
    private readonly ClrTypeDefinition _declaringType;
    private readonly IReadOnlyList<ClrParameter> _parameters;

    public ClrConstructor(ClrTypeDefinition declaringType, IEnumerable<ClrParameter> parameters, ConstructorInfo constructorInfo) {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(constructorInfo);

        _declaringType = declaringType;
        _parameters = [.. parameters];
        ConstructorInfo = constructorInfo;
    }

    /// <summary>
    /// Gets the type constructed by this constructor.
    /// </summary>
    public override ClrTypeDefinition MemberTypeDefinition => _declaringType;

    /// <summary>
    /// Gets the declaring type definition that owns this constructor.
    /// </summary>
    public override ClrTypeDefinition DeclaringTypeDefinition => _declaringType;

    /// <summary>
    /// Gets the ordered constructor parameters.
    /// </summary>
    public override IEnumerable<ClrParameter> Parameters => _parameters;

    public override string Name => _declaringType.Name;

    /// <summary>
    /// Gets the underlying reflection <see cref="ConstructorInfo"/>.
    /// </summary>
    public ConstructorInfo ConstructorInfo { get; }

    /// <summary>
    /// Gets the constructor visibility.
    /// </summary>
    public override AccessModifier AccessModifier => ClrAccessModifierResolver.Resolve(ConstructorInfo);

    /// <summary>
    /// Gets whether this constructor is static.
    /// </summary>
    public override LifetimeModifier LifetimeModifier => ConstructorInfo.IsStatic ? LifetimeModifier.Static : LifetimeModifier.Instance;

    public override string ToString() => $"{DeclaringTypeDefinition}({string.Join(", ", _parameters)})";
}