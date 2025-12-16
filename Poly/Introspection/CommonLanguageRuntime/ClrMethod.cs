using System.Reflection;

using Poly.Interpretation;
using Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// Reflection-backed method member for a CLR type. Exposes the return type, declaring type,
/// name, and ordered parameters, and creates invocation accessors for interpretation.
/// Instances are immutable and safe for concurrent reads.
/// </summary>
[DebuggerDisplay("{MemberType} {DeclaringType}.{Name}")]
internal sealed class ClrMethod : ClrTypeMember {
    private readonly Lazy<ClrTypeDefinition> _memberType;
    private readonly ClrTypeDefinition _declaringType;
    private readonly MethodInfo _methodInfo;
    private readonly IEnumerable<ClrParameter> _parameters;
    private readonly string _name;

    public ClrMethod(Lazy<ClrTypeDefinition> memberType, ClrTypeDefinition declaringType, IEnumerable<ClrParameter> parameters, MethodInfo methodInfo) {
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
    public override ClrTypeDefinition MemberType => _memberType.Value;

    /// <summary>
    /// Gets the declaring type definition that owns this method.
    /// </summary>
    public override ClrTypeDefinition DeclaringType => _declaringType;

    /// <summary>
    /// Gets the ordered method parameters.
    /// </summary>
    public override IEnumerable<ClrParameter> Parameters => _parameters;

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

    /// <summary>
    /// Creates an accessor that invokes this method on <paramref name="instance"/>
    /// with the supplied <paramref name="arguments"/>.
    /// </summary>
    public override Value GetMemberAccessor(Value instance, params IEnumerable<Value>? arguments) {
        // Convert null to empty enumerable for parameterless method calls
        var args = arguments ?? Enumerable.Empty<Value>();
        return new ClrMethodInvocationInterpretation(this, instance, args);
    }

    public override string ToString() => $"{MemberType} {DeclaringType}.{Name}({string.Join(", ", _parameters)})";
}