using Poly.Introspection;

namespace Poly.Interpretation.Operators;

public sealed class InvocationOperator : Operator {
    public InvocationOperator(Value target, string methodName, params Value[] arguments) {
        Target = target;
        MethodName = methodName;
        Arguments = arguments;
    }

    public Value Target { get; }
    public string MethodName { get; }
    public Value[] Arguments { get; }

    private ITypeMember GetMethodDefinition(InterpretationContext context) {
        var targetTypeDef = Target.GetTypeDefinition(context);

        var argumentTypeDefs = Arguments
            .Select(arg => arg.GetTypeDefinition(context))
            .ToList();

        var method = targetTypeDef
            .GetMembers(MethodName)
            .SingleOrDefault(e =>
                e.Parameters is not null &&
                e.Parameters.Count() == argumentTypeDefs.Count &&
                e.Parameters.Select(f => f.ParameterTypeDefinition).SequenceEqual(argumentTypeDefs));

        if (method == null) {
            throw new InvalidOperationException($"Method '{MethodName}' not found on type '{targetTypeDef}' with the specified argument types.");
        }

        return method;
    }

    public override Expression BuildExpression(InterpretationContext context) {
        var method = GetMethodDefinition(context);
        var methodInvocation = method.GetMemberAccessor(Target, Arguments);
        return methodInvocation.BuildExpression(context);
    }

    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) {
        var method = GetMethodDefinition(context);
        return method.MemberTypeDefinition;
    }
}