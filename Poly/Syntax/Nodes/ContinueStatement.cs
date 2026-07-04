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
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        var target = context.GetMetadata<Poly.Interpretation.Analysis.Semantics.ResolvedJumpTarget>(this);
        if (target is null)
            throw new System.InvalidOperationException("continue outside loop");
        var env = context.GetMetadata<Poly.Syntax.Primitives.ExpansionEnvironment>(null)!;
        yield return new Poly.Syntax.Primitives.Goto(env.GetLoopBoundary(target.TargetNodeId).Latch);
    }
}