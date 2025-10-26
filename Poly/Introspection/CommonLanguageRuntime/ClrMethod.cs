using System.Reflection;

using Poly.Interpretation;
using Poly.Introspection.CommonLanguageRuntime.InterpretationHelpers;

namespace Poly.Introspection.CommonLanguageRuntime;

public class ClrMethod : IMethod {
    private readonly Lazy<ClrTypeDefinition> _returnType;
    private readonly ClrParameter[] _parameters;
    private readonly MethodInfo _methodInfo;

    public ClrMethod(MethodInfo methodInfo, ClrTypeDefinition declaringType, Lazy<ClrTypeDefinition> returnType, IEnumerable<ClrParameter> parameters) {
        ArgumentNullException.ThrowIfNull(methodInfo);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(parameters);

        _returnType = returnType;
        _parameters = parameters.ToArray();
        _methodInfo = methodInfo;
        DeclaringType = declaringType;
    }

    public string Name  => _methodInfo.Name;
    public ClrTypeDefinition DeclaringType { get; }
    public ClrTypeDefinition ReturnType => _returnType.Value;
    public IEnumerable<IParameter> Parameters => _parameters;
    public MethodInfo MethodInfo => _methodInfo;


    ITypeDefinition IMethod.ReturnTypeDefinition => ReturnType;
    IEnumerable<IParameter> IMethod.Parameters => Parameters;

    public Value GetMethodInvocation(Value target, params IEnumerable<Value> arguments) => new ClrMethodInvocationInterpretation(this, target, arguments);
}
