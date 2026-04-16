using System.Reflection;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// Reflection-backed constructor member for a CLR type.
/// Exposes the constructed type, declaring type, and ordered constructor parameters.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
internal sealed class ClrConstructor : ClrTypeMember, ITypeConstructor {
    private readonly ClrTypeDefinition _declaringType;
    private readonly ConstructorInfo _constructorInfo;
    private readonly IReadOnlyList<ClrParameter> _parameters;

    public ClrConstructor(ClrTypeDefinition declaringType, IEnumerable<ClrParameter> parameters, ConstructorInfo constructorInfo) {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(constructorInfo);

        _declaringType = declaringType;
        _parameters = parameters.ToArray();
        _constructorInfo = constructorInfo;
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

    IEnumerable<IParameter> ITypeConstructor.Parameters => _parameters;

    /// <summary>
    /// Gets the constructor display name.
    /// </summary>
    public override string Name => _declaringType.Name;

    /// <summary>
    /// Gets the underlying reflection <see cref="ConstructorInfo"/>.
    /// </summary>
    public ConstructorInfo ConstructorInfo => _constructorInfo;

    /// <summary>
    /// Gets whether this constructor is static.
    /// </summary>
    public override bool IsStatic => _constructorInfo.IsStatic;

    public override string ToString() => $"{DeclaringTypeDefinition}({string.Join(", ", _parameters)})";
}