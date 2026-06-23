using System.Linq.Expressions;
using System.Reflection;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record NewArrayOp(string? Alias = null, int? Size = null, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => Size is null ? 1 : 0;
    public override int PushCount => 1;

    private static readonly MethodInfo HeapAllocate = Ref<Heap>.Method(h => h.Allocate(null));
    private static readonly ConstructorInfo LongArrayCtor = typeof(long[]).GetConstructor([typeof(int)])!;

    public override Expression? ToExpression(CompilationContext ctx) {
        var heap = Property(ctx.State, "Heap");

        Expression sizeExpr;
        if (Size is { } fixedSize) {
            sizeExpr = Constant(fixedSize);
        }
        else {
            sizeExpr = ctx.ResolveValue(this, 0);
        }

        var longArr = New(LongArrayCtor, Convert(sizeExpr, typeof(int)));
        var handle = Call(heap, HeapAllocate, Convert(longArr, typeof(object)));
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), Convert(handle, typeof(long)));
    }
}