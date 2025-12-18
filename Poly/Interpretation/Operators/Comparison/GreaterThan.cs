namespace Poly.Interpretation.Operators.Comparison;

/// <summary>
/// Represents a greater-than comparison between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expression.GreaterThan"/> which tests if the left value is greater than the right value.
/// Corresponds to the <c>&gt;</c> operator in C#.
/// </remarks>
public sealed class GreaterThan(Value leftHandValue, Value rightHandValue) : BooleanOperator {
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
        return Expression.GreaterThan(leftExpr, rightExpr);
    }

    /// <inheritdoc />
    public override string ToString() => $"{LeftHandValue} > {RightHandValue}";
}