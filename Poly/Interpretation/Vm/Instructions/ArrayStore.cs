using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record ArrayStore(string? Alias = null, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 3;
    public override int PushCount => 0;

    public override Expression? ToExpression(CompilationContext ctx) {
        var rawSlots = ctx.HeapRawSlots;
        var value = ctx.ResolveValue(this, 0);
        var handle = ctx.ResolveValue(this, 1);
        var index = ctx.ResolveValue(this, 2);

        var arrLocal = Variable(typeof(long[]), $"_arr_{ctx.CurrentLabelIndex}");
        return Block(
            new[] { arrLocal },
            Assign(arrLocal, Convert(ArrayAccess(rawSlots, Convert(handle, typeof(int))), typeof(long[]))),
            Assign(ArrayAccess(arrLocal, Convert(index, typeof(int))), Convert(value, typeof(long))));
    }
}