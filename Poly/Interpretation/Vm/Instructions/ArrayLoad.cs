using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record ArrayLoad(string? Alias = null, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 2;
    public override int PushCount => 1;

    public override Expression? ToExpression(CompilationContext ctx) {
        var rawSlots = ctx.HeapRawSlots;
        var handle = ctx.ResolveValue(this, 0);
        var index = ctx.ResolveValue(this, 1);

        var arrLocal = Variable(typeof(long[]), $"_arr_{ctx.CurrentLabelIndex}");
        return Block(
            new[] { arrLocal },
            Assign(arrLocal, Convert(ArrayAccess(rawSlots, Convert(handle, typeof(int))), typeof(long[]))),
            Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), ArrayAccess(arrLocal, Convert(index, typeof(int)))));
    }
}