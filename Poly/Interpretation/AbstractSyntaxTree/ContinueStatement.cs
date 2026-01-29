namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a continue statement that skips to the next iteration of a loop.
/// </summary>
/// <remarks>
/// Immediately transfers control to the next iteration of the innermost loop.
/// An optional label allows continuing an outer named loop.
/// </remarks>
public sealed record ContinueStatement(string? Label = null) : Operator {
    public override IEnumerable<Node?> Children => Enumerable.Empty<Node>();

    /// <inheritdoc />
    public override string ToString() => Label is not null ? $"continue {Label};" : "continue;";
}