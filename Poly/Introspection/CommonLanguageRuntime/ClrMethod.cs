using System.Reflection;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// Reflection-backed method member for a CLR type. Exposes the return type, declaring type,
/// name, and ordered parameters, and creates invocation accessors for interpretation.
/// Instances are immutable and safe for concurrent reads.
/// </summary>
[DebuggerDisplay("{MemberType} {DeclaringType}.{Name}")]
internal sealed class ClrMethod : ClrTypeMember, ITypeMethod {
    private readonly Lazy<ClrTypeDefinition> _memberType;
    private readonly ClrTypeDefinition _declaringType;
    private readonly MethodInfo _methodInfo;
    private readonly IEnumerable<ClrParameter> _parameters;
    private readonly string _name;

    public ClrMethod(Lazy<ClrTypeDefinition> memberType, ClrTypeDefinition declaringType, IEnumerable<ClrParameter> parameters, MethodInfo methodInfo)
    {
        ArgumentNullException.ThrowIfNull(memberType);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(methodInfo);

        _memberType = memberType;
        _declaringType = declaringType;
        _parameters = parameters;
        _methodInfo = methodInfo;
        _name = methodInfo.Name;
    }

    /// <summary>
    /// Gets the return type definition.
    /// </summary>
    public override ClrTypeDefinition MemberTypeDefinition => _memberType.Value;

    /// <summary>
    /// Gets the declaring type definition that owns this method.
    /// </summary>
    public override ClrTypeDefinition DeclaringTypeDefinition => _declaringType;

    /// <summary>
    /// Gets the ordered method parameters.
    /// </summary>
    public override IEnumerable<ClrParameter> Parameters => _parameters;

    /// <summary>
    /// Explicit implementation for ITypeMethod.Parameters to guarantee non-null.
    /// </summary>
    IEnumerable<IParameter> ITypeMethod.Parameters => _parameters;

    /// <summary>
    /// Gets the method name.
    /// </summary>
    public override string Name => _name;

    /// <summary>
    /// Gets the underlying reflection <see cref="MethodInfo"/>.
    /// </summary>
    public MethodInfo MethodInfo => _methodInfo;

    /// <summary>
    /// Gets whether this method is static.
    /// </summary>
    public override bool IsStatic => _methodInfo.IsStatic;

    public override string ToString() => $"{MemberTypeDefinition} {DeclaringTypeDefinition}.{Name}({string.Join(", ", _parameters)})";
}