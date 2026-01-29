namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a constant value in an interpretation tree.
/// </summary>
/// <remarks>
/// Constants are immutable values that are known at interpretation time.
/// This sealed record serves as a marker to distinguish constant values from mutable variables or parameters.
/// Type information is resolved by semantic analysis passes (INodeAnalyzer implementations).
/// </remarks>
public sealed record Constant(object? Value) : Node {
    /// <inheritdoc />
    public override string ToString() => Value?.ToString() ?? "null";
}