namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a break statement that exits a loop or switch statement.
/// </summary>
/// <remarks>
/// Immediately terminates the current loop or switch block and transfers control to the statement following the block.
/// An optional label allows breaking out of outer named loops or blocks.
/// </remarks>
public sealed record BreakStatement(string? Label = null) : Operator {
    public override IEnumerable<Node?> Children => Enumerable.Empty<Node>();

    /// <inheritdoc />
    public override string ToString() => Label is not null ? $"break {Label};" : "break;";
}