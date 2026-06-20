using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record IncSlot(int Offset, long Increment, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 0;
    public override int PushCount => 1;

    public override Expression? ToExpression(CompilationContext ctx) {
        var access = ArrayAccess(ctx.RawSlots,
            Add(Property(ctx.State, "FrameBase"), Constant(Offset)));
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), AddAssign(access, Constant(Increment)));
    }
}