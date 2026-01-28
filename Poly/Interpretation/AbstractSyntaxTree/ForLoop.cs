namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a for loop statement that repeats a body with an initializer, condition, and increment.
/// </summary>
/// <remarks>
/// The initializer is executed once, then the body repeats as long as the condition is true,
/// with the increment executed after each iteration. All components are optional.
/// Loop statements are executed for side effects rather than producing values.
/// </remarks>
public sealed record ForLoop(Node? Initializer, Node? Condition, Node? Increment, Node Body) : Operator {
    public override IEnumerable<Node?> Children => [Initializer, Condition, Increment, Body];

    /// <inheritdoc />
    public override string ToString()
    {
        var init = Initializer?.ToString() ?? "";
        var cond = Condition?.ToString() ?? "";
        var incr = Increment?.ToString() ?? "";
        return $"for ({init}; {cond}; {incr}) {{ {Body} }}";
    }
}