using System.Linq.Expressions;
using System.Reflection;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record LoadCapture(int Index, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 0;
    public override int PushCount => 1;

    private static readonly MethodInfo HandleLoadUpvalueMethod =
        typeof(Vm).GetMethod(nameof(Vm.HandleLoadUpvalue), [typeof(VmState), typeof(int)])
            ?? throw new InvalidOperationException("HandleLoadUpvalue not found");

    public override Expression? ToExpression(CompilationContext ctx) {
        var state = ctx.State;
        return Assign(ctx.ValueSlot(ctx.CurrentLabelIndex),
            Call(HandleLoadUpvalueMethod, state, Constant(Index)));
    }
}