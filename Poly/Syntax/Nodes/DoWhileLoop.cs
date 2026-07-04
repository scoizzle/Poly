namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a do-while loop statement that repeats a body until a condition becomes false.
/// </summary>
/// <remarks>
/// The body is executed at least once, then repeatedly as long as the condition evaluates to true.
/// Loop statements are executed for side effects rather than producing values.
/// </remarks>
public sealed record DoWhileLoop(Node Body, Node Condition, string? Label = null) : Statement {
    public override IEnumerable<Node?> Children => [Body, Condition];

    /// <inheritdoc />
    public override string ToString() => $"do {{ {Body} }} while ({Condition})";

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        var env = context.GetMetadata<Poly.Syntax.Primitives.ExpansionEnvironment>(null);
        if (env is null)
            throw new System.InvalidOperationException("ExpansionEnvironment not set");

        var bodyLabel = new Poly.Syntax.Primitives.Label("dowhile_body");
        var condLabel = new Poly.Syntax.Primitives.Label("dowhile_cond");
        var exit = new Poly.Syntax.Primitives.Label("dowhile_exit");

        env.RegisterLoopBoundary(this.Id, new Poly.Syntax.Primitives.LoopBoundary(exit, condLabel));

        // Body (executes at least once)
        yield return bodyLabel;
        using (env.EnterStatementContext()) {
            foreach (var p in Body.ToPrimitives(context))
                yield return p;
        }
        yield return new Poly.Syntax.Primitives.Discard();

        // Condition — CondGoto jumps when condition is 0 (false), exited by Goto(bodyLabel) below
        yield return condLabel;
        foreach (var p in Condition.ToPrimitives(context))
            yield return p;
        yield return new Poly.Syntax.Primitives.CondGoto(exit);

        // Back to body
        yield return new Poly.Syntax.Primitives.Goto(bodyLabel);

        // Exit
        yield return exit;
    }
}