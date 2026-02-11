namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a member access operation (property, field, or method access) in an interpretation tree.
/// </summary>
/// <remarks>
/// This operator enables accessing members of a value using dot notation (e.g., <c>person.Name</c>).
/// Member resolution happens in semantic analysis passes (INodeAnalyzer implementations) using type information from the context.
/// </remarks>
public sealed record MemberAccess(Node Value, string MemberName) : Operator {
    public override IEnumerable<Node?> Children => [Value];

    /// <inheritdoc />
    public override string ToString() => $"{Value}.{MemberName}";
}