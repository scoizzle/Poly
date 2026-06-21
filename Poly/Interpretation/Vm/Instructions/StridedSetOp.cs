using System.Linq.Expressions;

namespace Poly.Interpretation.Vm.Instructions;

/// <summary>
/// High-level strided bit-set operation that runs the entire loop in one
/// compiled expression (no µop dispatch per iteration).
/// Stack: [arr_handle, startValue, step, limit] → []
/// For each j = startValue; j &lt;= limit; j += step:
///   arr[j &gt;&gt; 6] |= 1L &lt;&lt; (int)(j &amp; 63)
/// </summary>
public sealed record StridedSetOp(NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 4;
    public override int PushCount => 0;

    public override Expression? ToExpression(CompilationContext ctx) {
        var handle = ctx.ResolveValue(this, 0);
        var start = ctx.ResolveValue(this, 1);
        var step = ctx.ResolveValue(this, 2);
        var limit = ctx.ResolveValue(this, 3);

        var arrLocal = Expression.Variable(typeof(long[]), "_arr");
        var j = Expression.Variable(typeof(long), "_j");
        var loop = Expression.Label("_loop");
        var done = Expression.Label("_done");

        return Expression.Block(
            new[] { arrLocal, j },
            Expression.Assign(arrLocal,
                Expression.Convert(
                    Expression.ArrayAccess(ctx.HeapRawSlots, Expression.Convert(handle, typeof(int))),
                    typeof(long[]))),
            Expression.Assign(j, start),
            Expression.Label(loop),
            Expression.IfThenElse(
                Expression.LessThanOrEqual(j, limit),
                Expression.Block(
                    Expression.Assign(
                        Expression.ArrayAccess(arrLocal,
                            Expression.Convert(Expression.RightShift(j, Expression.Constant(6)), typeof(int))),
                        Expression.Or(
                            Expression.ArrayAccess(arrLocal,
                                Expression.Convert(Expression.RightShift(j, Expression.Constant(6)), typeof(int))),
                            Expression.LeftShift(Expression.Constant(1L),
                                Expression.Convert(Expression.And(j, Expression.Constant(63L)), typeof(int))))),
                    Expression.Assign(j, Expression.Add(j, step)),
                    Expression.Goto(loop)),
                Expression.Label(done)));
    }
}