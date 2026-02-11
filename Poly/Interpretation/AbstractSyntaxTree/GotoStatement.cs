namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a goto statement that transfers control to a labeled location.
/// </summary>
/// <remarks>
/// Immediately transfers control to the specified label.
/// The target label must be defined within the same function scope.
/// </remarks>
public sealed record GotoStatement(string Target) : Operator {
    public override IEnumerable<Node?> Children => Enumerable.Empty<Node>();

    /// <inheritdoc />
    public override string ToString() => $"goto {Target};";
}