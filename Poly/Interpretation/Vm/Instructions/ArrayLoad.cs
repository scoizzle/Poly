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

        var arr = Convert(ArrayAccess(rawSlots, Convert(handle, typeof(int))), typeof(long[]));
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), ArrayAccess(arr, Convert(index, typeof(int))));
    }
}