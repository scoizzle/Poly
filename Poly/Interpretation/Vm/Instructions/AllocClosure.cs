using System.Linq.Expressions;
using System.Reflection;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record AllocClosure(int FuncIndex, int CaptureCount, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => CaptureCount;
    public override int PushCount => 1;

    private static readonly MethodInfo HandleAllocClosureMethod =
        Ref<VmState>.Method(s => Vm.HandleAllocClosure(s, default, default));

    public override Expression? ToExpression(CompilationContext ctx) {
        var state = ctx.State;
        var slots = ctx.RawSlots;
        var sp = Property(Property(state, "Stack"), "StackPointer");
        var regs = ctx.Registers;

        var body = new List<Expression>();
        // Pop captures from ring registers onto the value stack
        for (int i = 0; i < PopCount; i++) {
            var cap = ctx.ResolveValue(this, i);
            body.Add(Assign(ArrayAccess(slots, sp), cap));
            body.Add(Call(Property(state, "Stack"), "SetStackPointer", null, Add(sp, Constant(1))));
        }

        // Spill ring registers, call handler, restore
        body.Add(CtxPushRegisters(ctx));
        body.Add(Assign(ctx.StateProgramCounter, Constant(ctx.CurrentLabelIndex)));
        body.Add(Call(HandleAllocClosureMethod, state, Constant(FuncIndex), Constant(CaptureCount)));
        body.Add(Assign(ctx.ProgramCounter, ctx.StateProgramCounter));
        body.Add(CtxPopRegisters(ctx));

        // Result (heap handle) is on top of value stack
        var rv = ctx.ValueSlot(ctx.CurrentLabelIndex);
        body.Add(Assign(rv, ArrayAccess(slots,
            Subtract(sp, Constant(1)))));
        return Block(body);
    }

    internal static Expression CtxPushRegisters(CompilationContext ctx) {
        var regs = ctx.Registers;
        var state = ctx.State;
        var body = new List<Expression>();
        for (int k = 0; k < ctx.RingRegisterCount; k++)
            body.Add(Assign(ArrayAccess(regs, Constant(k)), ctx.RingSlot(k)));
        body.Add(Assign(Property(state, nameof(VmState.SavedRingDepth)), Constant(ctx.RingRegisterCount)));
        return Block(body);
    }

    internal static Expression CtxPopRegisters(CompilationContext ctx) {
        var regs = ctx.Registers;
        var state = ctx.State;
        var savedDepth = Property(state, nameof(VmState.SavedRingDepth));
        var body = new List<Expression>(ctx.RingRegisterCount + 1);
        for (int k = 0; k < ctx.RingRegisterCount; k++)
            body.Add(IfThen(GreaterThan(savedDepth, Constant(k)),
                Assign(ctx.RingSlot(k), ArrayAccess(regs, Constant(k)))));
        return Block(body);
    }
}