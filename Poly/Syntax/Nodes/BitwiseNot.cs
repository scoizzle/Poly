namespace Poly.Syntax.Nodes;

public sealed record BitwiseNot(Node Operand) : Operator {
    public override IEnumerable<Node?> Children => [Operand];
    public override string ToString() => $"~{Operand}";
}