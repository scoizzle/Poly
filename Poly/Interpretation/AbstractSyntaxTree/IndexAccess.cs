namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents an index access operation (indexer access) in an interpretation tree.
/// </summary>
/// <remarks>
/// This operator enables accessing indexed members of a value using bracket notation (e.g., <c>array[0]</c> or <c>dictionary["key"]</c>).
/// Indexer resolution happens in semantic analysis passes (INodeAnalyzer implementations) using type information from the context.
/// </remarks>
public sealed record IndexAccess(Node Value, params Node[] Arguments) : Operator {
    public override IEnumerable<Node?> Children => [Value, .. Arguments];

    /// <inheritdoc />
    public override string ToString() => $"{Value}[{string.Join(", ", Arguments)}]";
}