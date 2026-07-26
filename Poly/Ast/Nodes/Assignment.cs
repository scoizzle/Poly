namespace Poly.Ast.Nodes;

/// <summary>
/// Represents an assignment operation that assigns a value to a destination.
/// </summary>
/// <remarks>
/// The destination must be an assignable expression (variable, parameter, member, etc.).
/// Type information is resolved by semantic analysis passes (INodeAnalyzer implementations).
/// </remarks>
public sealed record Assignment(Node Destination, Node Value) : Expression {
    public override IEnumerable<Node?> Children => [Value, Destination];

    /// <inheritdoc />
    public override string ToString() => $"{Destination} = {Value}";

}