using System.Linq.Expressions;
using System.Reflection;

namespace Poly.Interpretation.VirtualMachine;

internal static class ProgramCompiler {
    // ── Cached PropertyInfo / MethodInfo via compile-time-safe expression trees ──
    static readonly PropertyInfo StackProp = MemberHelper.PropertyOf(() => default(VmState)!.Stack);
    static readonly PropertyInfo RawSlotsProp = MemberHelper.PropertyOf(() => default(ValueStack)!.RawSlots);
    static readonly MethodInfo SetSP = MemberHelper.MethodOf(() => default(ValueStack)!.SetSP(default));
    static readonly PropertyInfo FB = MemberHelper.PropertyOf(() => default(VmState)!.FrameBase);
    static readonly PropertyInfo CAS = MemberHelper.PropertyOf(() => default(VmState)!.CachedArgSlots);

    // ── Breakpoint check & entry/exit sync ──
    static readonly PropertyInfo PcProp = MemberHelper.PropertyOf(() => default(VmState)!.PC);
    static readonly PropertyInfo SpProp = MemberHelper.PropertyOf(() => default(ValueStack)!.SP);
    static readonly PropertyInfo DebugModeProp = MemberHelper.PropertyOf(() => default(VmState)!.DebugMode);
    static readonly PropertyInfo BPPcsProp = MemberHelper.PropertyOf(() => default(VmState)!.BreakpointPCs);
    static readonly PropertyInfo SavedPCProp = MemberHelper.PropertyOf(() => default(VmState)!.SavedPC);
    static readonly PropertyInfo StatusProp = MemberHelper.PropertyOf(() => default(VmState)!.Status);
    static readonly MethodInfo ContainsMethod = MemberHelper.MethodOf(() => default(HashSet<int>)!.Contains(default));

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
            Expression.Assign(sp, Expression.Property(stack, SpProp)),
            Expression.Assign(pc, Expression.Property(s, PcProp)),
            Expression.Loop(loopBody, breakTarget),
            Expression.Call(stack, SetSP, sp),
            Expression.Assign(Expression.Property(s, PcProp), pc));

        return Expression.Lambda<Action<VmState>>(final, s).Compile();
    }
}