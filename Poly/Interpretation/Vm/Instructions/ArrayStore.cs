using System.Linq.Expressions;
using System.Reflection;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record ArrayStore(string? Alias = null, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 3;
    public override int PushCount => 0;

    private static readonly MethodInfo HeapGet = typeof(Heap).GetMethod(nameof(Heap.Get), [typeof(int)])!;

    public override Expression? ToExpression(CompilationContext ctx) {
        var heap = Property(ctx.State, "Heap");
        var value = ctx.ResolveValue(this, 0);
        var handle = ctx.ResolveValue(this, 1);
        var index = ctx.ResolveValue(this, 2);

        var arrObj = Call(heap, HeapGet, Convert(handle, typeof(int)));
        var arr = Convert(arrObj, typeof(long[]));
        return Assign(ArrayAccess(arr, Convert(index, typeof(int))), Convert(value, typeof(long)));
    }
}