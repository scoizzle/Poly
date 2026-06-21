using System.Linq.Expressions;
using System.Reflection;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

/// <summary>
/// Stack effect: none. Spills: yes. State deps: Breakpoints, Registers, Status.
/// Checks if the current ProgramCounter is in the Breakpoints array.
/// Uses <see cref="CompilationContext.CurrentLabelIndex"/> to identify
/// this µop's PC — the check passes this PC to <see cref="Vm.HasBreakpoint"/>.
/// </summary>
public sealed record BreakpointCheck(NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 0;
    public override int PushCount => 0;

    private static readonly MethodInfo HasBreakpointMethod =
        Ref<VmState>.Method(s => Vm.HasBreakpoint(s, default));

    public override Expression? ToExpression(CompilationContext ctx) {
        // Check breakpoints at THIS µop's PC (not ProgramCounter which
        // may be stale — no default advance updates it).
        var check = Call(HasBreakpointMethod, ctx.State, Constant(ctx.CurrentLabelIndex));
        int depth = ctx.GetRingDepth(ctx.CurrentLabelIndex);
        var spill = Call.CtxPushRegisters(ctx);
        return IfThen(check, Block(spill,
            Assign(Property(ctx.State, "ProgramCounter"), Constant(ctx.CurrentLabelIndex + 1)),
            Assign(Property(ctx.State, nameof(VmState.SavedRingDepth)), Constant(depth)),
            Assign(Property(ctx.State, nameof(VmState.NeedsRingRestore)), Constant(true)),
            Assign(Property(ctx.State, "Status"), Constant(InterpreterStatus.Suspended)),
            Goto(ctx.ExitLabel)));
    }
}