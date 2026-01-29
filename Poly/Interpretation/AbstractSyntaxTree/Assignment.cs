namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents an assignment operation that assigns a value to a destination.
/// </summary>
/// <remarks>
/// The destination must be an assignable expression (variable, parameter, member, etc.).
/// Type information is resolved by semantic analysis passes (INodeAnalyzer implementations).
/// </remarks>
public sealed record Assignment(Node Destination, Node Value) : Operator {
    public override IEnumerable<Node?> Children => [Destination, Value];

    /// <inheritdoc />
    public override string ToString() => $"{Destination} = {Value}";
}