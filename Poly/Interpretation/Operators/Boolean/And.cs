namespace Poly.Interpretation.Operators.Boolean;

/// <summary>
/// Represents a logical AND operation (short-circuiting) between two boolean values.
/// </summary>
/// <remarks>
/// Compiles to <see cref="Expression.AndAlso"/> which implements short-circuit evaluation:
/// if the left operand is false, the right operand is not evaluated.
/// Corresponds to the <c>&amp;&amp;</c> operator in C#.
/// </remarks>
public sealed class And(Value leftHandValue, Value rightHandValue) : BooleanOperator {
    /// <summary>
    /// Gets the left-hand operand of the AND operation.
    /// </summary>
    public Value LeftHandValue { get; } = leftHandValue ?? throw new ArgumentNullException(nameof(leftHandValue));
    
    /// <summary>
    /// Gets the right-hand operand of the AND operation.
    /// </summary>
    public Value RightHandValue { get; } = rightHandValue ?? throw new ArgumentNullException(nameof(rightHandValue));

    /// <inheritdoc />
    public override Expression BuildExpression(InterpretationContext context) {
        var leftExpression = LeftHandValue.BuildExpression(context);
        var rightExpression = RightHandValue.BuildExpression(context);
        return Expression.AndAlso(leftExpression, rightExpression);
    }

    /// <inheritdoc />
    public override string ToString() => $"{LeftHandValue} && {RightHandValue}";
}