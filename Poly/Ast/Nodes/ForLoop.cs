namespace Poly.Ast.Nodes;

/// <summary>
/// Represents a for loop statement that repeats a body with an initializer, condition, and increment.
/// </summary>
/// <remarks>
/// The initializer is executed once, then the body repeats as long as the condition is true,
/// with the increment executed after each iteration. All components are optional.
/// Loop statements are executed for side effects rather than producing values.
/// </remarks>
/// <param name="DeclaredVariables">Loop-local variables the initializer declares
/// (<c>for (var i = 0; …)</c> / <c>for (int i = 0; …)</c>). They are in scope for the
/// condition, increment, and body and out of scope after the loop. Null for the plain
/// assignment form, where the initializer writes a variable declared in an enclosing scope.</param>
public sealed record ForLoop(Node? Initializer, Node? Condition, Node? Increment, Node Body, string? Label = null, IReadOnlyList<Variable>? DeclaredVariables = null) : Statement {
    public override IEnumerable<Node?> Children => [Initializer, Condition, Body, Increment];

    /// <inheritdoc />
    public override string ToString() {
        var init = Initializer?.ToString() ?? "";
        var cond = Condition?.ToString() ?? "";
        var incr = Increment?.ToString() ?? "";
        return $"for ({init}; {cond}; {incr}) {{ {Body} }}";
    }

}