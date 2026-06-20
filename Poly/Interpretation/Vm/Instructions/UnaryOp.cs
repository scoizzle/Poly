using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record UnaryOp(UnaryOpKind Kind, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 1;
    public override int PushCount => 1;

    public override Expression? ToExpression(CompilationContext ctx) {
        var operand = ctx.ResolveValue(this, 0);
        Expression expr = Kind switch {
            UnaryOpKind.Neg => Negate(operand),
            UnaryOpKind.Not => Condition(Equal(operand, Zero), One, Zero),
            UnaryOpKind.BitNot => Not(operand),
            _ => throw new ArgumentOutOfRangeException(nameof(Kind))
        };
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), expr);
    }

    static readonly Expression Zero = Constant(0L);
    static readonly Expression One = Constant(1L);
}