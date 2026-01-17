namespace Poly.Interpretation.Expressions;

/// <summary>
/// Represents a unary operation on a single operand.
/// </summary>
public sealed class UnaryOperation(UnaryOperationKind operation, Interpretable operand) : Interpretable {
    /// <summary>
    /// Gets the type of unary operation represented by this operator.
    /// </summary>
    public UnaryOperationKind Operation { get; } = operation;

    /// <summary>
    /// Gets the operand of the unary operation.
    /// </summary>
    public Interpretable Operand { get; } = operand ?? throw new ArgumentNullException(nameof(operand));

    /// <inheritdoc />
    public override TExpr Evaluate<TExpr, TStmt, TParam>(IExecutionPlanBuilder<TExpr, TStmt, TParam> builder)
    {
        var value = Operand.Evaluate(builder);
        return Operation switch {
            UnaryOperationKind.Negate => builder.Negate(value),
            UnaryOperationKind.Not => builder.LogicalNot(value),
            _ => throw new NotSupportedException($"Unsupported unary op {Operation}")
        };
    }
}