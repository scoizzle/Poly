using System.Linq.Expressions;
namespace Poly.Interpretation.Vm.Instructions;

public sealed record ArrayStore(string? Alias = null, NodeId? AstSource = null) : Instruction(AstSource) {
    public override int PopCount => 3;
    public override int PushCount => 0;
    public override Expression? ToExpression(CompilationContext ctx) => null;
}