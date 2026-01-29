namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a do-while loop statement that repeats a body until a condition becomes false.
/// </summary>
/// <remarks>
/// The body is executed at least once, then repeatedly as long as the condition evaluates to true.
/// Loop statements are executed for side effects rather than producing values.
/// </remarks>
public sealed record DoWhileLoop(Node Body, Node Condition) : Operator {
    public override IEnumerable<Node?> Children => [Body, Condition];

    /// <inheritdoc />
    public override string ToString() => $"do {{ {Body} }} while ({Condition})";
}