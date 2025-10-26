using Poly.Introspection;

namespace Poly.Interpretation.Operators;

public sealed class InvocationOperator : Operator
{
    public InvocationOperator(Value target, string methodName, params Value[] arguments)
    {
        Target = target;
        MethodName = methodName;
        Arguments = arguments;
    }

    public Value Target { get; }
    public string MethodName { get; }
    public Value[] Arguments { get; }

    private IMethod GetMethodDefinition(InterpretationContext context) {
        var targetTypeDef = Target.GetTypeDefinition(context);

        var argumentTypeDefs = Arguments
            .Select(arg => arg.GetTypeDefinition(context))
            .ToList();

        var method = targetTypeDef.Methods
            .Where(e => e.Name == MethodName)
            .Where(e => e.Parameters.Count() == argumentTypeDefs.Count)
            .Where(e => e.Parameters.Select(f => f.ParameterTypeDefinition).SequenceEqual(argumentTypeDefs))
            .SingleOrDefault();

        if (method == null) {
            throw new InvalidOperationException($"Method '{MethodName}' not found on type '{targetTypeDef}' with the specified argument types.");
        }

        return method;
    }

    public override Expression BuildExpression(InterpretationContext context) {
        var method = GetMethodDefinition(context);
        var methodInvocation = method.GetMethodInvocation(Target, Arguments);
        return methodInvocation.BuildExpression(context);
    }

    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) {
        var method = GetMethodDefinition(context);
        return method.ReturnTypeDefinition;
    }
}
