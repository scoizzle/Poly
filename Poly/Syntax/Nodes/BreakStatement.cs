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
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        var target = context.GetMetadata<Interpretation.Analysis.Semantics.ResolvedJumpTarget>(this);
        if (target is null)
            throw new InvalidOperationException("break outside loop");
        var env = context.Env;
        yield return new Primitives.Goto(env.GetLoopBoundary(target.TargetNodeId).Exit);
    }
}