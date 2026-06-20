using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

/// <summary>
/// Stack effect: pop(N), push(1). Spills: no.
/// Calls a CLR method directly (no call site indirection).
/// The <see cref="MethodInfo"/> is resolved at compile time by the lowering.
/// </summary>
public sealed record CallExternalDirect(MethodInfo Target, int ArgSlots, bool IsStatic = false, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => ArgSlots;
    public override int PushCount => 1;

    public override Expression? ToExpression(CompilationContext ctx) {
        var rawArgs = new Expression[ArgSlots];
        for (int i = 0; i < ArgSlots; i++)
            rawArgs[i] = ctx.ResolveValue(this, i);

        // Convert VM values (long) to the parameter types
        var paramInfos = Target.GetParameters();
        for (int i = 0; i < paramInfos.Length; i++) {
            int argIdx = IsStatic ? i : i + 1;
            if (paramInfos[i].ParameterType == typeof(int))
                rawArgs[argIdx] = Convert(rawArgs[argIdx], typeof(int));
            else if (paramInfos[i].ParameterType == typeof(short))
                rawArgs[argIdx] = Convert(rawArgs[argIdx], typeof(short));
            else if (paramInfos[i].ParameterType == typeof(byte))
                rawArgs[argIdx] = Convert(rawArgs[argIdx], typeof(byte));
            else if (paramInfos[i].ParameterType == typeof(double))
                rawArgs[argIdx] = Convert(rawArgs[argIdx], typeof(double));
            else if (paramInfos[i].ParameterType == typeof(float))
                rawArgs[argIdx] = Convert(rawArgs[argIdx], typeof(float));
            else if (paramInfos[i].ParameterType == typeof(bool))
                rawArgs[argIdx] = NotEqual(rawArgs[argIdx], Constant(0L));
        }

        // For instance methods, convert the instance parameter
        if (!IsStatic && ArgSlots > 0) {
            var instanceType = Target.DeclaringType;
            if (instanceType == typeof(int) || instanceType == typeof(long))
                rawArgs[0] = Convert(rawArgs[0], instanceType);
        }

        Expression? instance = IsStatic ? null : rawArgs[0];
        var callArgs = IsStatic ? rawArgs : rawArgs.Skip(1).ToArray();

        var call = Call(instance, Target, callArgs);

        // For void returns, the call is a side effect
        if (Target.ReturnType == typeof(void))
            return call;

        // Convert result to long for the VM's uniform type system
        Expression result = call;
        if (Target.ReturnType == typeof(int)) result = Convert(result, typeof(long));
        else if (Target.ReturnType == typeof(long)) { }
        else if (Target.ReturnType == typeof(bool)) result = Condition(result, Constant(1L), Constant(0L));
        else if (Target.ReturnType == typeof(double)) result = Convert(result, typeof(long));
        else result = Convert(result, typeof(long));

        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), result);
    }
}