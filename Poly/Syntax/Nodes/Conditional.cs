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

        // Condition
        foreach (var p in Condition.ToPrimitives(context))
            yield return p;
        yield return new Poly.Syntax.Primitives.CondGoto(elseLabel);

        // True branch (returns directly)
        foreach (var p in IfTrue.ToPrimitives(context))
            yield return p;
        yield return new Poly.Syntax.Primitives.Return();

        // False branch (returns directly)
        yield return elseLabel;
        foreach (var p in IfFalse.ToPrimitives(context))
            yield return p;
        yield return new Poly.Syntax.Primitives.Return();
    }
}