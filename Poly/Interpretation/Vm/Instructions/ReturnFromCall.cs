using System.Linq.Expressions;

using static System.Linq.Expressions.Expression;

namespace Poly.Interpretation.Vm.Instructions;

public sealed record ReturnFromCall(int ArgSlots, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 1;
    public override int PushCount => 0;

    public override Expression? ToExpression(CompilationContext ctx) {
        var returnVal = ConsumedFromPcs is { Length: > 0 }
            ? (Expression)ctx.ResolveValue(this, 0)
            : Constant(0L);

        var slots = ctx.RawSlots;
        var fb = Property(ctx.State, "FrameBase");
        var packedVar = Variable(typeof(long), "packed");

        return Block(
            [packedVar],
            Assign(packedVar, ArrayAccess(slots, Add(fb, Constant(ArgSlots)))),
            Assign(ArrayAccess(slots, fb), returnVal),
            Call(Property(ctx.State, "Stack"), "SetStackPointer", null, Add(fb, Constant(1))),
            Assign(ctx.StateProgramCounter,
                Convert(RightShift(packedVar, Constant(32)), typeof(int))),
            Assign(ctx.ProgramCounter, ctx.StateProgramCounter),
            Assign(fb, Convert(packedVar, typeof(int))),
            Goto(ctx.EntryLabel));
    }
}