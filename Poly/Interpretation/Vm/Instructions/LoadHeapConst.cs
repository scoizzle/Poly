using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

/// <summary>
/// Pushes a reference to a heap-allocated constant onto the eval stack.
/// The <see cref="Value"/> is the original CLR object (used by
/// <c>ProgramCompiler</c> to build <c>VmProgram.Constants</c>).
/// The <see cref="Handle"/> is the index into that constants array,
/// pre-loaded into <c>VmState.Heap</c> before execution.
/// Stack effect: push(1). Spills: no.
/// </summary>
public sealed record LoadHeapConst(object Value, int Handle, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 0;
    public override int PushCount => 1;

    public override Expression? ToExpression(CompilationContext ctx) {
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex), Constant((long)Handle));
    }
}