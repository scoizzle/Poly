using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record BinOp(BinOpKind Kind, long? Immediate = null, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => Immediate is null ? 2 : 1;
    public override int PushCount => 1;

    public override Expression? ToExpression(CompilationContext ctx) {
        var lhs = ctx.ResolveValue(this, 0);
        var rhs = Immediate is { } imm
            ? (Expression)Constant(imm)
            : (Expression)ctx.ResolveValue(this, 1);

        if (Kind is BinOpKind.Shl or BinOpKind.Shr) {
            var shiftRhs = Immediate is { } immVal
                ? (Expression)Constant((int)immVal)
                : Convert(rhs, typeof(int));
            var shifted = Kind == BinOpKind.Shl ? LeftShift(lhs, shiftRhs) : RightShift(lhs, shiftRhs);
            return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), shifted);
        }

        Expression expr = Kind switch {
            BinOpKind.Add => Add(lhs, rhs),
            BinOpKind.Sub => Subtract(lhs, rhs),
            BinOpKind.Mul => Multiply(lhs, rhs),
            BinOpKind.Div => Divide(lhs, rhs),
            BinOpKind.Mod => Modulo(lhs, rhs),
            BinOpKind.And => And(lhs, rhs),
            BinOpKind.Or => Or(lhs, rhs),
            BinOpKind.Xor => ExclusiveOr(lhs, rhs),
            BinOpKind.Eq => Condition(Equal(lhs, rhs), One, Zero),
            BinOpKind.Ne => Condition(NotEqual(lhs, rhs), One, Zero),
            BinOpKind.Lt => Condition(LessThan(lhs, rhs), One, Zero),
            BinOpKind.Le => Condition(LessThanOrEqual(lhs, rhs), One, Zero),
            BinOpKind.Gt => Condition(GreaterThan(lhs, rhs), One, Zero),
            BinOpKind.Ge => Condition(GreaterThanOrEqual(lhs, rhs), One, Zero),
            _ => throw new ArgumentOutOfRangeException(nameof(Kind))
        };
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), expr);
    }

    static readonly Expression Zero = Constant(0L);
    static readonly Expression One = Constant(1L);
}