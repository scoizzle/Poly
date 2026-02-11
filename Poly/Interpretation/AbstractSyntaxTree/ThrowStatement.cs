namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a throw statement that raises an exception.
/// </summary>
/// <remarks>
/// Immediately terminates normal execution and transfers control to exception handling.
/// The exception expression provides the error information to propagate to callers or exception handlers.
/// </remarks>
public sealed record ThrowStatement(Node Exception) : Operator {
    public override IEnumerable<Node?> Children => [Exception];

    /// <inheritdoc />
    public override string ToString() => $"throw {Exception};";
}