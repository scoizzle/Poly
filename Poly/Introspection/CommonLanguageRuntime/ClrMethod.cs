using System.Reflection;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// Reflection-backed method member for a CLR type. Exposes the return type, declaring type,
/// name, and ordered parameters, and creates invocation accessors for interpretation.
/// Instances are immutable and safe for concurrent reads.
/// </summary>
[DebuggerDisplay("{MemberType} {DeclaringType}.{Name}")]
internal sealed class ClrMethod : ClrTypeMember, ITypeMethod {
    private readonly Lazy<ClrTypeDefinition> _memberTypeResolver;

    public ClrMethod(Lazy<ClrTypeDefinition> memberTypeResolver, ClrTypeDefinition declaringType, IEnumerable<ClrParameter> parameters, MethodInfo methodInfo) {
        ArgumentNullException.ThrowIfNull(memberTypeResolver);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(methodInfo);

        _memberTypeResolver = memberTypeResolver;
        DeclaringTypeDefinition = declaringType;
        Parameters = [.. parameters];
        Name = methodInfo.Name;
        MethodInfo = methodInfo;
    }

    /// <summary>
    /// Gets the return type definition.
    /// </summary>
    public override ClrTypeDefinition MemberTypeDefinition => _memberTypeResolver.Value;

    /// <summary>
    /// Gets the declaring type definition that owns this method.
    /// </summary>
    public override ClrTypeDefinition DeclaringTypeDefinition { get; }

    /// <summary>
    /// Gets the ordered method parameters.
    /// </summary>
    public override IEnumerable<ClrParameter> Parameters { get; }

    /// <summary>
    /// Explicit implementation for ITypeMethod.Parameters to guarantee non-null.
    /// </summary>
    IEnumerable<IParameter> ITypeMethod.Parameters => Parameters;

    /// <summary>
    /// Gets the method name.
    /// </summary>
    public override string Name { get; }

    /// <summary>
    /// Gets the underlying reflection <see cref="MethodInfo"/>.
    /// </summary>
    public MethodInfo MethodInfo { get; }

    /// <summary>
    /// Gets the method visibility.
    /// </summary>
    public override AccessModifier AccessModifier => ClrAccessModifierResolver.Resolve(MethodInfo);

    /// <summary>
    /// Gets whether this method is static.
    /// </summary>
    public override LifetimeModifier LifetimeModifier => MethodInfo.IsStatic ? LifetimeModifier.Static : LifetimeModifier.Instance;

    public override string ToString() => $"{MemberTypeDefinition} {DeclaringTypeDefinition}.{Name}({string.Join(", ", Parameters)})";
}