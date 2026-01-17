namespace Poly.Interpretation.Expressions;

/// <summary>
/// Represents a conditional (if) expression that evaluates one of two statements based on a condition.
/// </summary>
public sealed class ConditionalStatement(Interpretable condition, Interpretable ifTrue, Interpretable ifFalse) : Interpretable {
    /// <summary>
    /// Gets the condition to evaluate.
    /// </summary>
    public Interpretable Condition { get; } = condition ?? throw new ArgumentNullException(nameof(condition));
    /// <summary>
    /// Gets the value to return if the condition is true.
    /// </summary>
    public Interpretable IfTrue { get; } = ifTrue ?? throw new ArgumentNullException(nameof(ifTrue));

    /// <summary>
    /// Gets the value to return if the condition is false.
    /// </summary>
    public Interpretable IfFalse { get; } = ifFalse ?? throw new ArgumentNullException(nameof(ifFalse));

    /// <inheritdoc />
    public override TExpr Evaluate<TExpr, TStmt, TParam>(IExecutionPlanBuilder<TExpr, TStmt, TParam> builder)
    {
        var condition = Condition.Evaluate(builder);
        var ifTrue = IfTrue.Evaluate(builder);
        var ifFalse = IfFalse.Evaluate(builder);
        return builder.Ternary(condition, ifTrue, ifFalse);
    }

    /// <inheritdoc />
    public override string ToString() => $"({Condition} ? {IfTrue} : {IfFalse})";
}