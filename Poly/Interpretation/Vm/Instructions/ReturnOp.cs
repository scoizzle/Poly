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
        var fb = Property(ctx.State, "FrameBase");
        var targetSlot = Condition(Equal(fb, Constant(-1)), Constant(0), fb);

        return Block(
            Assign(ArrayAccess(slots, targetSlot), returnVal),
            Call(Property(ctx.State, "Stack"), "SetStackPointer", null,
                Add(targetSlot, Constant(1))),
            Goto(ctx.ExitLabel));
    }
}