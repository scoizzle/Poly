namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a while loop statement that repeats a body while a condition is true.
/// </summary>
/// <remarks>
/// The body is executed repeatedly as long as the condition evaluates to true.
/// Loop statements are executed for side effects rather than producing values.
/// </remarks>
public sealed record WhileLoop(Node Condition, Node Body) : Statement {
    public override IEnumerable<Node?> Children => [Condition, Body];

    /// <inheritdoc />
    public override string ToString() => $"while ({Condition}) {{ {Body} }}";

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        var env = context.GetMetadata<Poly.Syntax.Primitives.ExpansionEnvironment>(null);
        if (env is null)
            throw new System.InvalidOperationException("ExpansionEnvironment not set");

        var header = new Poly.Syntax.Primitives.Label("while_header");
        var bodyLabel = new Poly.Syntax.Primitives.Label("while_body");
        var exit = new Poly.Syntax.Primitives.Label("while_exit");

        env.PushLoop(new Poly.Syntax.Primitives.LoopBoundary(exit, header));

        // Jump to header (skip past first entry to avoid re-executing sibling code)
        yield return new Poly.Syntax.Primitives.Goto(header);

        // Body block — drain the body's net ring effect so each
        // iteration is ring-neutral.  Body primitives are cached so we
        // can compute netPush without iterating twice.
        yield return bodyLabel;
        var bodyPrims = default(List<Poly.Syntax.Primitives.PrimitiveNode>)!;
        using (env.EnterStatementContext()) {
            bodyPrims = Body.ToPrimitives(context).ToList();
        }
        int bodyNetPush = 0;
        foreach (var p in bodyPrims) {
            var (pop, push) = p.StackEffect;
            bodyNetPush += push - pop;
        }
        foreach (var p in bodyPrims)
            yield return p;
        for (int i = 0; i < bodyNetPush; i++)
            yield return new Poly.Syntax.Primitives.Discard();

        // Header: condition check — CondGoto jumps when condition is 0 (false)
        yield return header;
        foreach (var p in Condition.ToPrimitives(context))
            yield return p;
        yield return new Poly.Syntax.Primitives.CondGoto(exit);

        // Back to body
        yield return new Poly.Syntax.Primitives.Goto(bodyLabel);

        // Exit
        yield return exit;
    }
}