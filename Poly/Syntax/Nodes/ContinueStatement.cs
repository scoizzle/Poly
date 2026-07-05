namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a continue statement that skips to the next iteration of a loop.
/// </summary>
/// <remarks>
/// Immediately transfers control to the next iteration of the innermost loop.
/// An optional label allows continuing an outer named loop.
/// </remarks>
public sealed record ContinueStatement(string? Label = null) : Statement {
    public override IEnumerable<Node?> Children => Enumerable.Empty<Node>();

    /// <inheritdoc />
    public override string ToString() => Label is not null ? $"continue {Label};" : "continue;";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        var target = context.GetMetadata<Interpretation.Analysis.Semantics.ResolvedJumpTarget>(this);
        if (target is null)
            throw new InvalidOperationException("continue outside loop");
        var env = context.Env;
        yield return new Primitives.Goto(env.GetLoopBoundary(target.TargetNodeId).Latch);
    }
}