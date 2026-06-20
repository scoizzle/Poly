using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;
namespace Poly.Interpretation.Vm.Instructions;

public sealed record ArrayLoad(string? Alias = null, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 2;
    public override int PushCount => 1;
    public override Expression? ToExpression(CompilationContext ctx) {
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), Constant(0L));
    }
}