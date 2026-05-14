namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents an await expression that extracts the result from an awaitable operation.
/// </summary>
public sealed record Await(Node Operand) : Operator {
    public override IEnumerable<Node?> Children => [Operand];

    public override string ToString() => $"await {Operand}";
}