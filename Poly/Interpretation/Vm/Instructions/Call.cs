using System.Linq.Expressions;
using System.Reflection;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record Call(int FuncIndex, int ArgSlots, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => ArgSlots;
    public override int PushCount => 1;

    private static readonly MethodInfo HandleCallMethod =
        Ref<VmState>.Method(s => Vm.HandleCall(s, default, default));

    public override Expression? ToExpression(CompilationContext ctx) {
        var state = ctx.State;
        var slots = ctx.RawSlots;
        var sp = Property(Property(state, "Stack"), "StackPointer");
        var regs = ctx.Registers;

        var body = new List<Expression>();
        for (int i = 0; i < PopCount; i++) {
            var arg = ctx.ResolveValue(this, i);
            body.Add(Assign(ArrayAccess(slots, sp), arg));
            body.Add(Call(Property(state, "Stack"), "SetStackPointer", null, Add(sp, Constant(1))));
        }

        body.Add(CtxPushRegisters(ctx));
        body.Add(Assign(ctx.StateProgramCounter, Constant(ctx.CurrentLabelIndex)));
        body.Add(Call(HandleCallMethod, state, Constant(FuncIndex), Constant(ArgSlots)));
        body.Add(Assign(ctx.ProgramCounter, ctx.StateProgramCounter));
        body.Add(CtxPopRegisters(ctx));

        var rv = ctx.ValueSlot(ctx.CurrentLabelIndex);
        body.Add(Assign(rv, ArrayAccess(slots,
            Subtract(Property(Property(state, "Stack"), "StackPointer"), Constant(1)))));
        body.Add(Goto(ctx.EntryLabel));
        return Block(body);
    }

    internal static Expression CtxPushRegisters(CompilationContext ctx) {
        int count = ctx.CurrentLabelIndex;
        if (count < 0) return Empty();
        var stmts = new Expression[count + 1];
        for (int i = 0; i <= count; i++)
            stmts[i] = Assign(ArrayAccess(ctx.Registers, Constant(i)), ctx.ValueSlot(i));
        return Block(stmts);
    }

    internal static Expression CtxPopRegisters(CompilationContext ctx) {
        int count = ctx.CurrentLabelIndex;
        if (count < 0) return Empty();
        var stmts = new Expression[count + 1];
        for (int i = 0; i <= count; i++)
            stmts[i] = Assign(ctx.ValueSlot(i), ArrayAccess(ctx.Registers, Constant(i)));
        return Block(stmts);
    }
}