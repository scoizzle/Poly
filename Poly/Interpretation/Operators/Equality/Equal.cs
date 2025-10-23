namespace Poly.Interpretation.Operators.Equality;

/// <summary>
/// Represents an equality comparison between two values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expression.Equal"/> which tests if two values are equal.
/// Corresponds to the <c>==</c> operator in C#.
/// </remarks>
public sealed class Equal(Value leftHandValue, Value rightHandValue) : BooleanOperator {
    /// <summary>
    /// Gets the left-hand operand of the equality comparison.
    /// </summary>
    public Value LeftHandValue { get; init; } = leftHandValue ?? throw new ArgumentNullException(nameof(leftHandValue));
    
    /// <summary>
    /// Gets the right-hand operand of the equality comparison.
    /// </summary>
    public Value RightHandValue { get; init; } = rightHandValue ?? throw new ArgumentNullException(nameof(rightHandValue));

    /// <inheritdoc />
    public override Expression BuildExpression(InterpretationContext context) {
        Expression leftExpr = LeftHandValue.BuildExpression(context);
        Expression rightExpr = RightHandValue.BuildExpression(context);
        return Expression.Equal(leftExpr, rightExpr);
    }

    /// <inheritdoc />
    public override string ToString() => $"{LeftHandValue} == {RightHandValue}";
}