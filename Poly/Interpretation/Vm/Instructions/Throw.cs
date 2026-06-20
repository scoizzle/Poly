using System.Linq.Expressions;
namespace Poly.Interpretation.Vm.Instructions;

public sealed record Throw(NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 1;
    public override int PushCount => 0;
    public override Expression? ToExpression(CompilationContext ctx) => null;
}