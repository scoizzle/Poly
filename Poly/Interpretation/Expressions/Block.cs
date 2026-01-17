namespace Poly.Interpretation.Expressions;

/// <summary>
/// Represents a block of expressions to be executed in sequence.
/// </summary>
public sealed class Block(params IEnumerable<Interpretable> expressions) : Interpretable {
    /// <summary>
    /// Gets the sequence of expressions to execute.
    /// </summary>
    public IEnumerable<Interpretable> Expressions { get; } = expressions;

    /// <inheritdoc />

    public override TExpr Evaluate<TExpr, TStmt, TParam>(IExecutionPlanBuilder<TExpr, TStmt, TParam> builder)
    {
        var expressions = Expressions.Select(expr => expr.Evaluate(builder));
        return builder.ExprBlock(expressions);
    }

    public override string ToString() => $"{{ {string.Join("; ", Expressions)}; }}";
}