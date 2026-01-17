namespace Poly.Interpretation.Expressions;

public sealed class Assignment(Interpretable destination, Interpretable value) : Interpretable {
    /// <summary>
    /// Gets the destination of the assignment (left-hand side).
    /// </summary>
    public Interpretable Destination { get; } = destination ?? throw new ArgumentNullException(nameof(destination));

    /// <summary>
    /// Gets the value to assign (right-hand side).
    /// </summary>
    public Interpretable Value { get; } = value ?? throw new ArgumentNullException(nameof(value));

    /// <inheritdoc />
    public override TExpr Evaluate<TExpr, TStmt, TParam>(IExecutionPlanBuilder<TExpr, TStmt, TParam> builder)
    {
        var dest = Destination.Evaluate(builder);
        var val = Value.Evaluate(builder);
        return builder.AssignExpr(dest, val);
    }
}