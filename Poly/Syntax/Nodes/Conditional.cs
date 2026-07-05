using Poly.Syntax.Primitives;

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
    public override IEnumerable<PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        var elseLabel = new Label("ternary_else");
        var mergeLabel = new Label("ternary_merge");

        // Condition
        foreach (var p in Condition.ToPrimitives(context))
            yield return p;
        yield return new CondGoto(elseLabel);

        // True branch
        foreach (var p in IfTrue.ToPrimitives(context))
            yield return p;
        yield return new Goto(mergeLabel);

        // False branch
        yield return elseLabel;
        foreach (var p in IfFalse.ToPrimitives(context))
            yield return p;

        // Merge: Phi annotation.
        yield return mergeLabel;
        yield return new Phi();
    }
}