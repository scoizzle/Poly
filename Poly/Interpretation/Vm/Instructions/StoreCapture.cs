using System.Linq.Expressions;
using System.Reflection;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record StoreCapture(int Index, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 1;
    public override int PushCount => 0;

    private static readonly MethodInfo HandleStoreUpvalueMethod =
        typeof(Vm).GetMethod(nameof(Vm.HandleStoreUpvalue), [typeof(VmState), typeof(int), typeof(long)])
            ?? throw new InvalidOperationException("HandleStoreUpvalue not found");

    public override Expression? ToExpression(CompilationContext ctx) {
        var state = ctx.State;
        var value = ctx.ResolveValue(this, 0);
        return Call(HandleStoreUpvalueMethod, state, Constant(Index), value);
    }
}