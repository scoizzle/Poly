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
        var env = context.GetMetadata<Poly.Syntax.Primitives.ExpandEnv>(null);
        if (env is null || !env.IsInLoop)
            throw new System.InvalidOperationException("continue outside loop");
        yield return new Poly.Syntax.Primitives.Goto(env.CurrentLoop.Latch);
    }
}