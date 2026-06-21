using System.Linq.Expressions;
using System.Reflection;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record Jump(int Target, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 0;
    public override int PushCount => 0;

    private static readonly ConstructorInfo InvalidOpCtor =
        typeof(InvalidOperationException).GetConstructor([typeof(string)])!;

    public override Expression? ToExpression(CompilationContext ctx) {
        if (!ctx.LimitLoops)
            return Goto(ctx.GetLabel(Target));

        // Loop limit check using locals computed once in the preamble.
        var loopCtr = Property(ctx.State, nameof(VmState.LoopCounters));
        var counter = ArrayAccess(loopCtr, Constant(Target));

        return Block(
            IfThen(
                AndAlso(
                    ctx.LoopLimitActive,
                    GreaterThanOrEqual(PreIncrementAssign(counter), ctx.LoopMaxIter)),
                Throw(New(InvalidOpCtor,
                    Constant($"Infinite loop detected: iteration limit exceeded at PC={Target}")))),
            Goto(ctx.GetLabel(Target)));
    }
}