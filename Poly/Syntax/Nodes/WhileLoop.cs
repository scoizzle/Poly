namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a while loop statement that repeats a body while a condition is true.
/// </summary>
/// <remarks>
/// The body is executed repeatedly as long as the condition evaluates to true.
/// Loop statements are executed for side effects rather than producing values.
/// </remarks>
public sealed record WhileLoop(Node Condition, Node Body, string? Label = null) : Statement {
    public override IEnumerable<Node?> Children => [Condition, Body];

    /// <inheritdoc />
    public override string ToString() => $"while ({Condition}) {{ {Body} }}";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        var env = context.Env;

        var header = new Primitives.Label("while_header");
        var bodyLabel = new Primitives.Label("while_body");
        var exit = new Primitives.Label("while_exit");

        env.RegisterLoopBoundary(Id, new Primitives.LoopBoundary(exit, header));

        // Header: condition check — CondGoto jumps when condition is 0 (false).
        // Entry falls through from the previous sibling; no initial Goto needed.
        yield return header;
        foreach (var p in Condition.ToPrimitives(context))
            yield return p;
        yield return new Primitives.CondGoto(exit);

        // Body block — drain the body's net ring effect so each
        // iteration is ring-neutral.  Body primitives are cached so we
        // can compute netPush without iterating twice.
        yield return bodyLabel;
        var bodyPrims = default(List<Primitives.PrimitiveNode>)!;
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
            yield return new Primitives.Discard();

        // Back to header (condition check)
        yield return new Primitives.Goto(header);

        // Exit
        yield return exit;
    }
}