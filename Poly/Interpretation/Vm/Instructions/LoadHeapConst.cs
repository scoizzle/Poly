using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

/// <summary>
/// Pushes a reference to a heap-allocated constant onto the eval stack.
/// The <see cref="Value"/> is the original CLR object.
/// The <see cref="Handle"/> is the index into <c>VmProgram.Constants</c>
/// — the <c>.rodata</c> section — allocated onto the heap at runtime
/// when this µop executes, avoiding upfront pre-loading.
/// Stack effect: push(1). Spills: no.
/// </summary>
public sealed record LoadHeapConst(object Value, int Handle, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 0;
    public override int PushCount => 1;

    private static readonly System.Reflection.MethodInfo _allocate =
        Ref<Heap>.Method(h => h.Allocate(null));

    public override Expression? ToExpression(CompilationContext ctx) {
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex),
            Convert(
                Call(ctx.Heap, _allocate, Convert(Constant(Value), typeof(object))),
                typeof(long)));
    }
}