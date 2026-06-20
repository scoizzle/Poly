using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record BranchIfFalse(int Target, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 1;
    public override int PushCount => 0;

    public override Expression? ToExpression(CompilationContext ctx) {
        var cond = ctx.ResolveValue(this, 0);
        return IfThen(
            Equal(cond, Constant(0L)),
            Goto(ctx.GetLabel(Target)));
    }
}