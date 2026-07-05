namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a for loop statement that repeats a body with an initializer, condition, and increment.
/// </summary>
/// <remarks>
/// The initializer is executed once, then the body repeats as long as the condition is true,
/// with the increment executed after each iteration. All components are optional.
/// Loop statements are executed for side effects rather than producing values.
/// </remarks>
public sealed record ForLoop(Node? Initializer, Node? Condition, Node? Increment, Node Body, string? Label = null) : Statement {
    public override IEnumerable<Node?> Children => [Initializer, Condition, Body, Increment];

    /// <inheritdoc />
    public override string ToString() {
        var init = Initializer?.ToString() ?? "";
        var cond = Condition?.ToString() ?? "";
        var incr = Increment?.ToString() ?? "";
        return $"for ({init}; {cond}; {incr}) {{ {Body} }}";
    }

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        var env = context.Env;

        var header = new Primitives.Label("for_header");
        var bodyLabel = new Primitives.Label("for_body");
        var exit = new Primitives.Label("for_exit");

        env.RegisterLoopBoundary(Id, new Primitives.LoopBoundary(exit, header));

        // Initializer (executed once before loop)
        if (Initializer is not null) {
            using var _0 = env.EnterStatementContext();
            foreach (var p in Initializer.ToPrimitives(context))
                yield return p;
            yield return new Primitives.Discard();
        }

        // Header: condition check — CondGoto jumps when condition is 0 (false).
        // Entry falls through from initializer; no initial Goto needed.
        yield return header;
        if (Condition is not null) {
            foreach (var p in Condition.ToPrimitives(context))
                yield return p;
            yield return new Primitives.CondGoto(exit);
        }

        // Body — fall through from condition when true
        yield return bodyLabel;
        using (env.EnterStatementContext()) {
            foreach (var p in Body.ToPrimitives(context))
                yield return p;
        }
        yield return new Primitives.Discard();

        // Increment (after body, before next condition check)
        if (Increment is not null) {
            using var _1 = env.EnterStatementContext();
            foreach (var p in Increment.ToPrimitives(context))
                yield return p;
            yield return new Primitives.Discard();
        }

        // Back to header (condition check)
        yield return new Primitives.Goto(header);

        // Exit
        yield return exit;
    }
}