namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a constant value in an interpretation tree.
/// </summary>
/// <remarks>
/// Constants are immutable values that are known at interpretation time and compile
/// to <see cref="Exprs.ConstantExpression"/> nodes. This sealed record
/// serves as a marker to distinguish constant values from mutable variables or parameters.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Constant(object? Value) : Node
{
    /// <inheritdoc />
    public override string ToString() => Value?.ToString() ?? "null";
}