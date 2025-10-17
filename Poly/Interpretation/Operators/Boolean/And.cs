namespace Poly.Interpretation.Operators.Boolean;

public sealed class And(Value leftHandValue, Value rightHandValue) : BooleanOperator {
    public Value LeftHandValue { get; init; } = leftHandValue ?? throw new ArgumentNullException(nameof(leftHandValue));
    public Value RightHandValue { get; init; } = rightHandValue ?? throw new ArgumentNullException(nameof(rightHandValue));

    public override Expression BuildExpression(Context context) {
        var leftExpression = LeftHandValue.BuildExpression(context);
        var rightExpression = RightHandValue.BuildExpression(context);
        return Expression.AndAlso(leftExpression, rightExpression);
    }

    public override string ToString() => $"{LeftHandValue} && {RightHandValue}";
}
