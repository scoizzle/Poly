namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents an assignment operation that assigns a value to a destination.
/// </summary>
/// <remarks>
/// Compiles to a <see cref="Exprs.BinaryNode"/> with 
/// <see cref="Exprs.NodeType.Assign"/> node type.
/// The destination must be an assignable expression (variable, parameter, member, etc.).
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Assignment(Node Destination, Node Value) : Operator
{
    /// <inheritdoc />
    public override string ToString() => $"{Destination} = {Value}";
}