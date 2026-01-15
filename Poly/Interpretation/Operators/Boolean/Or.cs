namespace Poly.Interpretation.Operators.Boolean;

/// <summary>
/// Represents a logical OR operation (short-circuiting) between two boolean values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expression.OrElse"/> which implements short-circuit evaluation:
/// if the left operand is true, the right operand is not evaluated.
/// Corresponds to the <c>||</c> operator in C#.
/// </remarks>
public sealed class Or(Value leftHandValue, Value rightHandValue) : BooleanOperator {
    /// <summary>
    /// Gets the left-hand operand of the OR operation.
    /// </summary>
    public Value LeftHandValue { get; } = leftHandValue ?? throw new ArgumentNullException(nameof(leftHandValue));

    /// <summary>
    /// Gets the right-hand operand of the OR operation.
    /// </summary>
    public Value RightHandValue { get; } = rightHandValue ?? throw new ArgumentNullException(nameof(rightHandValue));

    /// <inheritdoc />
    public override Expression BuildExpression(InterpretationContext context)
    {
        Expression leftExpr = LeftHandValue.BuildExpression(context);
        Expression rightExpr = RightHandValue.BuildExpression(context);
        return Expression.OrElse(leftExpr, rightExpr);
    }

    /// <inheritdoc />
    public override string ToString() => $"{LeftHandValue} || {RightHandValue}";
}