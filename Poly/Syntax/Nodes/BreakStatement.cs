namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a break statement that exits a loop or switch statement.
/// </summary>
/// <remarks>
/// Immediately terminates the current loop or switch block and transfers control to the statement following the block.
/// An optional label allows breaking out of outer named loops or blocks.
/// </remarks>
public sealed record BreakStatement(string? Label = null) : Statement {
    public override IEnumerable<Node?> Children => Enumerable.Empty<Node>();

    /// <inheritdoc />
    public override string ToString() => Label is not null ? $"break {Label};" : "break;";

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        var env = context.GetMetadata<Poly.Syntax.Primitives.ExpandEnv>(null);
        if (env is null || env.Loops.Count == 0)
            throw new System.InvalidOperationException("break outside loop");
        yield return new Poly.Syntax.Primitives.Goto(env.Loops.Peek().Exit);
    }
}