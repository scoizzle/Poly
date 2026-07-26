namespace Poly.Ast.Nodes;

public sealed record BitwiseXor(Node LeftHandValue, Node RightHandValue) : Expression {
    public override IEnumerable<Node?> Children => [LeftHandValue, RightHandValue];
    public override string ToString() => $"({LeftHandValue} ^ {RightHandValue})";

}