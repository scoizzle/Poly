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

    private static readonly MethodInfo HeapAllocate =
        Ref<Heap>.Method(h => h.Allocate(null));

    public override Expression? ToExpression(CompilationContext ctx) {
        var rawArgs = new Expression[ArgSlots];
        for (int i = 0; i < ArgSlots; i++)
            rawArgs[i] = ctx.ResolveValue(this, i);

        // Convert VM values (long) to the parameter types.
        // Stack-value types (numeric, bool) get direct Convert;
        // reference types get dereferenced from heap handles.
        var paramInfos = Target.GetParameters();
        for (int i = 0; i < paramInfos.Length; i++) {
            int argIdx = IsStatic ? i : i + 1;
            var paramType = paramInfos[i].ParameterType;
            var pt = paramType.GetPrimitiveType();
            if (pt is not null && pt.Value.IsStackValue()) {
                rawArgs[argIdx] = paramType == typeof(bool)
                    ? NotEqual(rawArgs[argIdx], Constant(0L))
                    : Convert(rawArgs[argIdx], paramType);
            }
            else if (!paramType.IsValueType) {
                // Reference-type parameter — dereference heap handle
                var handle = Convert(rawArgs[argIdx], typeof(int));
                var obj = ArrayAccess(ctx.HeapRawSlots, handle);
                rawArgs[argIdx] = Convert(obj, paramType);
            }
        }

        // For instance methods, convert the instance parameter
        if (!IsStatic && ArgSlots > 0) {
            var instanceType = Target.DeclaringType;
            var instPt = instanceType?.GetPrimitiveType();
            if (instPt is not null && instPt.Value.IsStackValue()) {
                rawArgs[0] = Convert(rawArgs[0], instanceType!);
            }
            else if (instanceType is not null && !instanceType.IsValueType) {
                // Instance is a heap handle — load the actual object from
                // state.Heap.RawSlots[handle] and cast to the declaring type.
                var handle = Convert(rawArgs[0], typeof(int));
                var obj = ArrayAccess(ctx.HeapRawSlots, handle);
                rawArgs[0] = Convert(obj, instanceType);
            }
        }

        Expression? instance = IsStatic ? null : rawArgs[0];
        var callArgs = IsStatic ? rawArgs : rawArgs.Skip(1).ToArray();

        var call = Call(instance, Target, callArgs);

        // For void returns, the call is a side effect
        if (Target.ReturnType == typeof(void))
            return call;

        // Convert result to long for the VM's uniform type system.
        Expression result = call;
        var returnType = Target.ReturnType;
        if (returnType != typeof(void)) {
            var retPt = returnType.GetPrimitiveType();
            if (retPt is not null && retPt.Value.IsStackValue()) {
                // Stack-value types convert directly to long.
                if (returnType == typeof(bool))
                    result = Condition(result, Constant(1L), Constant(0L));
                else if (returnType != typeof(long))
                    result = Convert(result, typeof(long));
            }
            else {
                // All other types (strings, CLR structs, domain entities):
                // allocate on heap, return handle.
                result = Convert(
                    Call(ctx.Heap, HeapAllocate, Convert(result, typeof(object))),
                    typeof(long));
            }
        }

        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), result);
    }
}