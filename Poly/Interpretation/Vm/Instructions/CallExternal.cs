using System.Linq.Expressions;
using System.Reflection;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record CallExternal(int SiteIndex, int ArgSlots, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => ArgSlots;
    public override int PushCount => 1;

    private static readonly MethodInfo HandleCallExternalMethod =
        Ref<VmState>.Method(s => Vm.HandleCallExternal(s, default));

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

        body.Add(Call.CtxPushRegisters(ctx));
        body.Add(Assign(ctx.StateProgramCounter, Constant(ctx.CurrentLabelIndex)));
        body.Add(Call(HandleCallExternalMethod, state, Constant(SiteIndex)));
        body.Add(Assign(ctx.ProgramCounter, ctx.StateProgramCounter));
        body.Add(Call.CtxPopRegisters(ctx));

        var rv = ctx.ValueSlot(ctx.CurrentLabelIndex);
        body.Add(Assign(rv, ArrayAccess(slots,
            Subtract(Property(Property(state, "Stack"), "StackPointer"), Constant(1)))));
        body.Add(Goto(ctx.EntryLabel));
        return Block(body);
    }
}