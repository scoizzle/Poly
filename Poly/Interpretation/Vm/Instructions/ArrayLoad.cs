using System.Linq.Expressions;
using System.Reflection;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record ArrayLoad(string? Alias = null, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 2;
    public override int PushCount => 1;

    private static readonly MethodInfo HeapGet = typeof(Heap).GetMethod(nameof(Heap.Get), [typeof(int)])!;

    public override Expression? ToExpression(CompilationContext ctx) {
        var heap = Property(ctx.State, "Heap");
        var handle = ctx.ResolveValue(this, 0);
        var index = ctx.ResolveValue(this, 1);

        var arrObj = Call(heap, HeapGet, Convert(handle, typeof(int)));
        var arr = Convert(arrObj, typeof(long[]));
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex),
            ArrayAccess(arr, Convert(index, typeof(int))));
    }
}