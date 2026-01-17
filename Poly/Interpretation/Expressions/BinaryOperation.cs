namespace Poly.Interpretation.Expressions;

public sealed class BinaryOperation(BinaryOperationKind operation, Interpretable leftHandValue, Interpretable rightHandValue) : Interpretable {
    /// <summary>
    /// Gets the type of binary operation represented by this operator.
    /// </summary>
    public BinaryOperationKind Operation { get; } = operation;

    /// <summary>
    /// Gets the left-hand operand of the binary operation.
    /// </summary>
    public Interpretable LeftHandValue { get; } = leftHandValue ?? throw new ArgumentNullException(nameof(leftHandValue));

    /// <summary>
    /// Gets the right-hand operand of the binary operation.
    /// </summary>
    public Interpretable RightHandValue { get; } = rightHandValue ?? throw new ArgumentNullException(nameof(rightHandValue));



    public override TExpr Evaluate<TExpr, TStmt, TParam>(IExecutionPlanBuilder<TExpr, TStmt, TParam> builder)
    {
        var left = LeftHandValue.Evaluate(builder);
        var right = RightHandValue.Evaluate(builder);
        return Operation switch {
            BinaryOperationKind.Add => builder.Add(left, right),
            BinaryOperationKind.Subtract => builder.Subtract(left, right),
            BinaryOperationKind.Multiply => builder.Multiply(left, right),
            BinaryOperationKind.Divide => builder.Divide(left, right),
            BinaryOperationKind.Modulo => builder.Modulus(left, right),
            BinaryOperationKind.And => builder.LogicalAnd(left, right),
            BinaryOperationKind.Or => builder.LogicalOr(left, right),
            BinaryOperationKind.Equal => builder.Equal(left, right),
            BinaryOperationKind.NotEqual => builder.NotEqual(left, right),
            BinaryOperationKind.LessThan => builder.LessThan(left, right),
            BinaryOperationKind.LessThanOrEqual => builder.LessThanOrEqual(left, right),
            BinaryOperationKind.GreaterThan => builder.GreaterThan(left, right),
            BinaryOperationKind.GreaterThanOrEqual => builder.GreaterThanOrEqual(left, right),
            BinaryOperationKind.Coalesce => builder.Coalesce(left, right),
            _ => throw new NotSupportedException($"Unsupported binary op {Operation}")
        };
    }
}