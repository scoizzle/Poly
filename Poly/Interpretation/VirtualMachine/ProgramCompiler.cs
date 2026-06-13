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

    // ── Breakpoint check reflection ──
    static readonly PropertyInfo DebugModeProp = typeof(VmState).GetProperty(nameof(VmState.DebugMode), BF)!;
    static readonly PropertyInfo BPPcsProp = typeof(VmState).GetProperty(nameof(VmState.BreakpointPCs), BF)!;
    static readonly PropertyInfo SavedPCProp = typeof(VmState).GetProperty(nameof(VmState.SavedPC), BF)!;
    static readonly PropertyInfo StatusProp = typeof(VmState).GetProperty(nameof(VmState.Status), BF)!;
    static readonly MethodInfo ContainsMethod = typeof(HashSet<int>).GetMethod("Contains", [typeof(int)])!;

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
        var suspendStatus = Expression.Constant(InterpreterStatus.Suspended);
        for (int i = 0; i < uops.Count; i++) {
            var uop = uops[i];
            // Normal execution block: trace → µop → pc++
            var execBody = new List<Expression> { uop.ToExpression(ctx) };
            execBody.Insert(0, ctx.TraceBefore(uop));
            if (uop is not JumpOp and not JumpIfFalseOp
                and not ReturnOp and not ReturnFromCallOp
                and not CallOp and not CallClosureOp)
                execBody.Add(Expression.Assign(pc, Expression.Add(pc, Expression.Constant(1))));
            execBody.Add(Expression.Empty());

            // Gated by breakpoint check — when DebugMode + BreakpointPCs.Contains(pc)
            // we suspend (skipping trace + µop + pc++ entirely).
            var breakCheck = Expression.IfThenElse(
                Expression.AndAlso(
                    Expression.Property(s, DebugModeProp),
                    Expression.AndAlso(
                        Expression.NotEqual(Expression.Property(s, BPPcsProp),
                            Expression.Constant(null, typeof(HashSet<int>))),
                        Expression.Call(Expression.Property(s, BPPcsProp),
                            ContainsMethod, pc))),
                Expression.Block(
                    Expression.Assign(Expression.Property(s, SavedPCProp), pc),
                    Expression.Assign(Expression.Property(s, StatusProp), suspendStatus),
                    Expression.Assign(pc, codeLen)),
                Expression.Block(typeof(void), execBody));

            switchCases[i] = Expression.SwitchCase(breakCheck, Expression.Constant(i));
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