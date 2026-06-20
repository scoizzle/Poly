using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record LoadSlot(int Offset, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 0;
    public override int PushCount => 1;

    public override Expression? ToExpression(CompilationContext ctx) {
        var index = Add(Property(ctx.State, "FrameBase"), Constant(Offset));
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), ArrayAccess(ctx.RawSlots, index));
    }
}