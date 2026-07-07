namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a return statement that exits a function and optionally returns a value.
/// </summary>
/// <remarks>
/// Immediately terminates function execution and returns to the caller.
/// An optional value may be returned to the caller; if absent, the function returns no value.
/// </remarks>
public sealed record Return(Node? Value = null) : Statement {
    public override IEnumerable<Node?> Children => Value is not null ? [Value] : Enumerable.Empty<Node>();

    /// <inheritdoc />
    public override string ToString() => Value is not null ? $"return {Value};" : "return;";

    public static Return Void => new();
    public static Return False => new(new Constant(false));
    public static Return True => new(new Constant(true));
    public static Return Null => new(new Constant(null));

}