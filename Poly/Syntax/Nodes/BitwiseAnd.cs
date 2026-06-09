namespace Poly.Syntax.Nodes;

public sealed record BitwiseAnd(Node LeftHandValue, Node RightHandValue) : Operator {
    public override IEnumerable<Node?> Children => [LeftHandValue, RightHandValue];
    public override string ToString() => $"({LeftHandValue} & {RightHandValue})";
}