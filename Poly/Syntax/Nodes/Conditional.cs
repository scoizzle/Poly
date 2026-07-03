namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a conditional (ternary) expression that evaluates one of two values based on a condition.
/// </summary>
/// <remarks>
/// Evaluates the condition and returns either the true value or the false value accordingly.
/// Corresponds to the <c>condition ? trueValue : falseValue</c> operator in C#.
/// Type information is resolved by semantic analysis passes (INodeAnalyzer implementations).
/// </remarks>
public sealed record Conditional(Node Condition, Node IfTrue, Node IfFalse) : Expression {
    public override IEnumerable<Node?> Children => [Condition, IfTrue, IfFalse];
    /// <inheritdoc />
    public override string ToString() => $"({Condition} ? {IfTrue} : {IfFalse})";

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        var elseLabel = new Poly.Syntax.Primitives.Label("ternary_else");
        var mergeLabel = new Poly.Syntax.Primitives.Label("ternary_merge");

        // Use a temp slot to store the result (avoids PHI)
        var env = context.GetMetadata<Poly.Syntax.Primitives.ExpandEnv>(null);
        if (env is null) {
            env = new Poly.Syntax.Primitives.ExpandEnv();
            context.SetMetadata<Poly.Syntax.Primitives.ExpandEnv>(null, env);
        }
        int tempSlot = env.AllocateTempSlot();

        // Condition
        foreach (var p in Condition.ToPrimitives(context))
            yield return p;
        yield return new Poly.Syntax.Primitives.CondGoto(elseLabel);

        // True branch
        foreach (var p in IfTrue.ToPrimitives(context))
            yield return p;
        yield return new Poly.Syntax.Primitives.StoreLocal(tempSlot);
        yield return new Poly.Syntax.Primitives.Goto(mergeLabel);

        // False branch
        yield return elseLabel;
        foreach (var p in IfFalse.ToPrimitives(context))
            yield return p;
        yield return new Poly.Syntax.Primitives.StoreLocal(tempSlot);

        // Merge: read result from temp slot
        yield return mergeLabel;
        yield return new Poly.Syntax.Primitives.LoadLocal(tempSlot);
    }
}