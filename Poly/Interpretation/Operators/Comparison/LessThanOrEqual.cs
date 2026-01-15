namespace Poly.Interpretation.Operators.Comparison;

/// <summary>
/// Represents a less-than-or-equal comparison between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expression.LessThanOrEqual"/> which tests if the left value is less than or equal to the right value.
/// Corresponds to the <c>&lt;=</c> operator in C#.
/// </remarks>
public sealed class LessThanOrEqual(Value leftHandValue, Value rightHandValue) : BooleanOperator {
    /// <summary>
    /// Gets the left-hand operand of the comparison.
    /// </summary>
    public Value LeftHandValue { get; } = leftHandValue ?? throw new ArgumentNullException(nameof(leftHandValue));

    /// <summary>
    /// Gets the right-hand operand of the comparison.
    /// </summary>
    public Value RightHandValue { get; } = rightHandValue ?? throw new ArgumentNullException(nameof(rightHandValue));

    /// <inheritdoc />
    public override Expression BuildExpression(InterpretationContext context)
    {
        Expression leftExpr = LeftHandValue.BuildExpression(context);
        Expression rightExpr = RightHandValue.BuildExpression(context);
        return Expression.LessThanOrEqual(leftExpr, rightExpr);
    }

    /// <inheritdoc />
    public override string ToString() => $"{LeftHandValue} <= {RightHandValue}";
}