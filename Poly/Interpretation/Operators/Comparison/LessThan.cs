namespace Poly.Interpretation.Operators.Comparison;

/// <summary>
/// Represents a less-than comparison between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expression.LessThan"/> which tests if the left value is less than the right value.
/// Corresponds to the <c>&lt;</c> operator in C#.
/// </remarks>
public sealed class LessThan(Value leftHandValue, Value rightHandValue) : BooleanOperator {
    /// <summary>
    /// Gets the left-hand operand of the comparison.
    /// </summary>
    public Value LeftHandValue { get; init; } = leftHandValue ?? throw new ArgumentNullException(nameof(leftHandValue));

    /// <summary>
    /// Gets the right-hand operand of the comparison.
    /// </summary>
    public Value RightHandValue { get; init; } = rightHandValue ?? throw new ArgumentNullException(nameof(rightHandValue));

    /// <inheritdoc />
    public override Expression BuildExpression(InterpretationContext context) {
        Expression leftExpr = LeftHandValue.BuildExpression(context);
        Expression rightExpr = RightHandValue.BuildExpression(context);
        return Expression.LessThan(leftExpr, rightExpr);
    }

    /// <inheritdoc />
    public override string ToString() => $"{LeftHandValue} < {RightHandValue}";
}