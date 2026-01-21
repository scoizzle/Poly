namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents an index access operation (indexer access) in an interpretation tree.
/// </summary>
/// <remarks>
/// This operator enables accessing indexed members of a value using bracket notation (e.g., <c>array[0]</c> or <c>dictionary["key"]</c>).
/// Indexer resolution happens in semantic analysis middleware using type information from the context,
/// not on the node itself.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record IndexAccess(Node Value, params Node[] Arguments) : Operator
{
    /// <inheritdoc />
    public override string ToString() => $"{Value}[{string.Join(", ", Arguments)}]";
}