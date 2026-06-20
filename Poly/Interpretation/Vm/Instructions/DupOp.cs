using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;
namespace Poly.Interpretation.Vm.Instructions;

public sealed record DupOp(NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 1;
    public override int PushCount => 2;
    public override Expression? ToExpression(CompilationContext ctx) {
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex),
            ctx.ResolveValue(this, 0));
    }
}