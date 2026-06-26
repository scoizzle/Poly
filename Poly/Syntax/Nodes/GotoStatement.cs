namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a goto statement that transfers control to a labeled location.
/// </summary>
/// <remarks>
/// Immediately transfers control to the specified label.
/// The target label must be defined within the same function scope.
/// </remarks>
public sealed record GotoStatement(string Target) : Statement {
    public override IEnumerable<Node?> Children => Enumerable.Empty<Node>();

    /// <inheritdoc />
    public override string ToString() => $"goto {Target};";

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        // A goto/label pair within the same function; uses a named label
        yield return new Poly.Syntax.Primitives.Goto(new Poly.Syntax.Primitives.Label(Target));
    }
}