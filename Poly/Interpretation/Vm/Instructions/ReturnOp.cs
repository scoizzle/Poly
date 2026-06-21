using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record ReturnOp(NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 1;
    public override int PushCount => 0;

    public override Expression? ToExpression(CompilationContext ctx) {
        var returnVal = ConsumedFromPcs is { Length: > 0 }
            ? (Expression)ctx.ResolveValue(this, 0)
            : Constant(0L);

        var slots = ctx.RawSlots;
        var targetSlot = Condition(Equal(ctx.FrameBase, Constant(-1)), Constant(0), ctx.FrameBase);

        return Block(
            Assign(ArrayAccess(slots, targetSlot), returnVal),
            Call(Property(ctx.State, "Stack"), "SetStackPointer", null,
                Add(targetSlot, Constant(1))),
            Goto(ctx.ExitLabel));
    }
}