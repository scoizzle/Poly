namespace Poly.Ast.Nodes;

public sealed record BitwiseNot(Node Operand) : Expression {
    public override IEnumerable<Node?> Children => [Operand];
    public override string ToString() => $"~{Operand}";

}