using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record Jump(int Target, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 0;
    public override int PushCount => 0;

    public override Expression? ToExpression(CompilationContext ctx) {
        return Goto(ctx.GetLabel(Target));
    }
}