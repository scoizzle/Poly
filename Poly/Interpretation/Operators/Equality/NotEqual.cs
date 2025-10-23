namespace Poly.Interpretation.Operators.Equality;

/// <summary>
/// Represents an inequality comparison between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expression.NotEqual"/> which tests if two values are not equal.
/// Corresponds to the <c>!=</c> operator in C#.
/// </remarks>
public sealed class NotEqual(Value leftHandValue, Value rightHandValue) : BooleanOperator {
    /// <summary>
    /// Gets the left-hand operand of the inequality comparison.
    /// </summary>
    public Value LeftHandValue { get; init; } = leftHandValue ?? throw new ArgumentNullException(nameof(leftHandValue));
    
    /// <summary>
    /// Gets the right-hand operand of the inequality comparison.
    /// </summary>
    public Value RightHandValue { get; init; } = rightHandValue ?? throw new ArgumentNullException(nameof(rightHandValue));

    /// <inheritdoc />
    public override Expression BuildExpression(InterpretationContext context) {
        Expression leftExpr = LeftHandValue.BuildExpression(context);
        Expression rightExpr = RightHandValue.BuildExpression(context);
        return Expression.NotEqual(leftExpr, rightExpr);
    }

    /// <inheritdoc />
    public override string ToString() => $"{LeftHandValue} != {RightHandValue}";
}