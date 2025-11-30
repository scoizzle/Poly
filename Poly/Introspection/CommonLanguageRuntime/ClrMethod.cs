using System.Reflection;

using Poly.Interpretation;
using Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

namespace Poly.Introspection.CommonLanguageRuntime;

[DebuggerDisplay("{MemberType} {DeclaringType}.{Name}")]
public sealed class ClrMethod : ClrTypeMember {
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

    public override ClrTypeDefinition MemberType => _memberType.Value;
    public override ClrTypeDefinition DeclaringType => _declaringType;
    public override IEnumerable<ClrParameter> Parameters => _parameters;
    public override string Name => _name;
    public MethodInfo MethodInfo => _methodInfo;

    public override Value GetMemberAccessor(Value instance, params IEnumerable<Value>? arguments) {
        ArgumentNullException.ThrowIfNull(arguments);

        return new ClrMethodInvocationInterpretation(this, instance, arguments);
    }

    public override string ToString() => $"{MemberType} {DeclaringType}.{Name}({string.Join(", ", _parameters)})";
}