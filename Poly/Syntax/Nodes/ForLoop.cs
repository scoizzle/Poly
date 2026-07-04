namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a for loop statement that repeats a body with an initializer, condition, and increment.
/// </summary>
/// <remarks>
/// The initializer is executed once, then the body repeats as long as the condition is true,
/// with the increment executed after each iteration. All components are optional.
/// Loop statements are executed for side effects rather than producing values.
/// </remarks>
public sealed record ForLoop(Node? Initializer, Node? Condition, Node? Increment, Node Body) : Statement {
    public override IEnumerable<Node?> Children => [Initializer, Condition, Body, Increment];

    /// <inheritdoc />
    public override string ToString() {
        var init = Initializer?.ToString() ?? "";
        var cond = Condition?.ToString() ?? "";
        var incr = Increment?.ToString() ?? "";
        return $"for ({init}; {cond}; {incr}) {{ {Body} }}";
    }

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        var env = context.GetMetadata<Poly.Syntax.Primitives.ExpansionEnvironment>(null);
        if (env is null)
            throw new System.InvalidOperationException("ExpansionEnvironment not set");

        var header = new Poly.Syntax.Primitives.Label("for_header");
        var bodyLabel = new Poly.Syntax.Primitives.Label("for_body");
        var exit = new Poly.Syntax.Primitives.Label("for_exit");

        env.RegisterLoopBoundary(this.Id, new Poly.Syntax.Primitives.LoopBoundary(exit, header));

        // Initializer (executed once before loop)
        if (Initializer is not null) {
            using var _0 = env.EnterStatementContext();
            foreach (var p in Initializer.ToPrimitives(context))
                yield return p;
            yield return new Poly.Syntax.Primitives.Discard();
        }

        // Header: condition check — CondGoto jumps when condition is 0 (false).
        // Entry falls through from initializer; no initial Goto needed.
        yield return header;
        if (Condition is not null) {
            foreach (var p in Condition.ToPrimitives(context))
                yield return p;
            yield return new Poly.Syntax.Primitives.CondGoto(exit);
        }

        // Body — fall through from condition when true
        yield return bodyLabel;
        using (env.EnterStatementContext()) {
            foreach (var p in Body.ToPrimitives(context))
                yield return p;
        }
        yield return new Poly.Syntax.Primitives.Discard();

        // Increment (after body, before next condition check)
        if (Increment is not null) {
            using var _1 = env.EnterStatementContext();
            foreach (var p in Increment.ToPrimitives(context))
                yield return p;
            yield return new Poly.Syntax.Primitives.Discard();
        }

        // Back to header (condition check)
        yield return new Poly.Syntax.Primitives.Goto(header);

        // Exit
        yield return exit;
    }
}