using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record StoreSlot(int Offset, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 1;
    public override int PushCount => 0;

    public override Expression? ToExpression(CompilationContext ctx) {
        var index = Add(ctx.FrameBase, Constant(Offset));
        var value = ctx.ResolveValue(this, 0);
        return Assign(ArrayAccess(ctx.RawSlots, index), value);
    }
}