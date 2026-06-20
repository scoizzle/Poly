using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;
namespace Poly.Interpretation.Vm.Instructions;

public sealed record AllocClosure(int FuncIndex, int CaptureCount, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => CaptureCount;
    public override int PushCount => 1;
    public override Expression? ToExpression(CompilationContext ctx) {
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), Constant(0L));
    }
}