using System.Linq.Expressions;
using System.Reflection;

using static System.Linq.Expressions.Expression;
namespace Poly.Interpretation.Vm.Instructions;

public sealed record BreakpointCheck(NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 0;
    public override int PushCount => 0;
    private static readonly MethodInfo HasBreakpointMethod =
        Ref<VmState>.Method(s => Vm.HasBreakpoint(s, default));
    public override Expression? ToExpression(CompilationContext ctx) {
        var check = Call(HasBreakpointMethod, ctx.State, Property(ctx.State, "ProgramCounter"));
        var spill = Call.CtxPushRegisters(ctx);
        return IfThen(check, Block(spill,
            Assign(Property(ctx.State, "Status"), Constant(InterpreterStatus.Suspended)),
            Goto(ctx.ExitLabel)));
    }
}