using System.Linq.Expressions;
using System.Reflection;

namespace Poly.Interpretation.VirtualMachine;

internal static class ProgramCompiler {
    const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly PropertyInfo StackProp = typeof(VmState).GetProperty(nameof(VmState.Stack), BF)!;
    static readonly PropertyInfo RawSlotsProp = typeof(ValueStack).GetProperty(nameof(ValueStack.RawSlots), BF)!;
    static readonly MethodInfo SetSP = typeof(ValueStack).GetMethod(nameof(ValueStack.SetSP), BF)!;
    static readonly PropertyInfo FB = typeof(VmState).GetProperty(nameof(VmState.FrameBase), BF)!;
    static readonly PropertyInfo CAS = typeof(VmState).GetProperty(nameof(VmState.CachedArgSlots), BF)!;

    /// <summary>Compile a dispatch-loop delegate.  The delegate reads
    /// <c>state.PC</c> at entry, loops dispatching via a switch on PC,
    /// and exits when <c>pc >= count</c>.  Call/Return/Jump all work
    /// because each iteration re-checks PC from the local variable.</summary>
    public static Action<VmState> Compile(IReadOnlyList<MicroOp> uops) {
        var s = Expression.Parameter(typeof(VmState), "s");
        var stack = Expression.Property(s, StackProp);
        var slots = Expression.Property(stack, RawSlotsProp);
        var sp = Expression.Parameter(typeof(int), "sp");
        var pc = Expression.Parameter(typeof(int), "pc");
        var fb = Expression.Property(s, FB);
        var cas = Expression.Property(s, CAS);
        var codeLen = Expression.Constant(uops.Count);
        var ctx = new CompilationContext(s, slots, sp, pc, fb, cas, codeLen);
        var breakTarget = Expression.Label("exit");

        // Each switch case: execute µop body; the µop advances PC itself
        // (or sets it for control flow). After the case, the loop
        // re-checks pc < count and dispatches again.
        var switchCases = new System.Linq.Expressions.SwitchCase[uops.Count];
        for (int i = 0; i < uops.Count; i++) {
            var body = new List<Expression> { uops[i].ToExpression(ctx) };
            // µops that don't modify pc get pc++
            if (uops[i] is not JumpOp and not JumpIfFalseOp
                and not ReturnOp and not ReturnFromCallOp
                and not CallOp and not CallClosureOp)
                body.Add(Expression.Assign(pc, Expression.Add(pc, Expression.Constant(1))));
            body.Add(Expression.Empty());
            switchCases[i] = Expression.SwitchCase(
                Expression.Block(typeof(void), body), Expression.Constant(i));
        }

        // Loop body: if pc < count → switch(pc) → case body → fall through → repeat
        //           otherwise → break
        var loopBody = Expression.IfThenElse(
            Expression.LessThan(pc, codeLen),
            Expression.Switch(pc, Expression.Break(breakTarget), switchCases),
            Expression.Break(breakTarget));

        var final = Expression.Block(
            [sp, pc, .. ctx.Variables],
            Expression.Assign(sp, Expression.Property(stack, "SP")),
            Expression.Assign(pc, Expression.Property(s, "PC")),
            Expression.Loop(loopBody, breakTarget),
            Expression.Call(stack, SetSP, sp),
            Expression.Assign(Expression.Property(s, "PC"), pc));

        return Expression.Lambda<Action<VmState>>(final, s).Compile();
    }
}