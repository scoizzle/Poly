using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;
namespace Poly.Interpretation.Vm.Instructions;

public sealed record StridedSet(string? Alias = null, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 0;
    public override int PushCount => 1;
    public override Expression? ToExpression(CompilationContext ctx) {
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), Constant(0L));
    }
}