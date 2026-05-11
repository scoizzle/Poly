namespace Poly.Syntax.Nodes;

public sealed record NullForgiving(Node Operand) : Node {
    public override IEnumerable<Node?> Children => [Operand];

    public override string ToString() => $"{Operand}!";
}