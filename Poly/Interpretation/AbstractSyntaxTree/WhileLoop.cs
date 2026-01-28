namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a while loop statement that repeats a body while a condition is true.
/// </summary>
/// <remarks>
/// The body is executed repeatedly as long as the condition evaluates to true.
/// Loop statements are executed for side effects rather than producing values.
/// </remarks>
public sealed record WhileLoop(Node Condition, Node Body) : Operator {
    public override IEnumerable<Node?> Children => [Condition, Body];

    /// <inheritdoc />
    public override string ToString() => $"while ({Condition}) {{ {Body} }}";
}