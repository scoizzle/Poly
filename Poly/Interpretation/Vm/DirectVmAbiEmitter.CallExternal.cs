using System.Linq.Expressions;
using System.Reflection;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm;

public static partial class DirectVmAbiEmitter {
    private static readonly MethodInfo InvokeHostMethod =
        typeof(DirectVmAbiEmitter).GetMethod(nameof(InvokeHost),
            BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Generic host-method dispatch for <see cref="CallExternal"/>. Looks up
    /// <paramref name="methodName"/> on <see cref="VmState.Host"/> by name and
    /// arity. Fail closed when Host is null or the method is missing.
    /// No domain types — any host object is valid.
    /// </summary>
    private static object? InvokeHost(VmState state, string methodName, object?[] args) {
        if (state.Host is null)
            throw new InvalidOperationException(
                $"CallExternal '{methodName}' requires VmState.Host.");

        var hostType = state.Host.GetType();
        const BindingFlags flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        MethodInfo? match = null;
        foreach (var candidate in hostType.GetMethods(flags)) {
            if (candidate.Name != methodName)
                continue;
            if (candidate.GetParameters().Length != args.Length)
                continue;
            match = candidate;
            break;
        }

        if (match is null)
            throw new InvalidOperationException(
                $"Host type '{hostType.Name}' does not define method '{methodName}' with {args.Length} parameter(s).");

        return match.Invoke(state.Host, args);
    }

    private static Expression EmitCallExternal(CallExternal call, AbiCtx ctx) {
        var argExprs = new List<Expression>(call.Arguments.Length);
        var argSlots = new int[call.Arguments.Length];
        for (int i = 0; i < call.Arguments.Length; i++) {
            argExprs.Add(CompileNode(call.Arguments[i], ctx));
            argSlots[i] = ctx.RingDepth - 1;
        }

        var argObjs = new Expression[call.Arguments.Length];
        for (int i = 0; i < call.Arguments.Length; i++) {
            // CallExternal arguments are host values (strings, objects) — ring
            // holds a heap handle after EmitConstant / Member / etc.
            argObjs[i] = Call(ctx.HeapLocal, HeapUnsafeGet,
                Convert(ctx.RingVar(argSlots[i]), typeof(int)));
        }

        var invoke = Call(
            null,
            InvokeHostMethod,
            ctx.State,
            Constant(call.MethodName),
            NewArrayInit(typeof(object), argObjs));

        int slot = ctx.AllocSlot();
        ctx.RingDepth = slot + 1;
        var body = new List<Expression>(argExprs.Count + 2);
        body.AddRange(argExprs);
        body.Add(invoke);
        body.Add(Assign(ctx.RingVar(slot), Constant(0L)));
        return Block(body);
    }
}