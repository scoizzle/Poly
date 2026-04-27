namespace Poly.Syntax.AbstractSyntaxTree;

/// <summary>
/// Represents a return statement that exits a function and optionally returns a value.
/// </summary>
/// <remarks>
/// Immediately terminates function execution and returns to the caller.
/// An optional value may be returned to the caller; if absent, the function returns no value.
/// </remarks>
public sealed record Return(Node? Value = null) : Operator {
    public override IEnumerable<Node?> Children => Value is not null ? [Value] : Enumerable.Empty<Node>();

    /// <inheritdoc />
    public override string ToString() => Value is not null ? $"return {Value};" : "return;";

    public static readonly Return Void = new();
    public static readonly Return False = new(new Constant(false));
    public static readonly Return True = new(new Constant(true));
    public static readonly Return Null = new(new Constant(null));
}