namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a using statement that manages resource disposal.
/// </summary>
/// <remarks>
/// The resource is acquired and the body is executed. Regardless of how the body completes,
/// the resource is released (via cleanup operations specific to the implementation language).
/// This pattern ensures deterministic resource management.
/// </remarks>
public sealed record UsingStatement(Node Resource, Node Body) : Operator {
    public override IEnumerable<Node?> Children => [Resource, Body];

    /// <inheritdoc />
    public override string ToString() => $"using ({Resource}) {{ {Body} }}";
}