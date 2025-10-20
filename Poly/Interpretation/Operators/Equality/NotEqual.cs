namespace Poly.Interpretation.Operators.Equality;

public sealed class NotEqual(Value leftHandValue, Value rightHandValue) : BooleanOperator {
    public Value LeftHandValue { get; init; } = leftHandValue ?? throw new ArgumentNullException(nameof(leftHandValue));
    public Value RightHandValue { get; init; } = rightHandValue ?? throw new ArgumentNullException(nameof(rightHandValue));

    public override Expression BuildExpression(Context context) {
        Expression leftExpr = LeftHandValue.BuildExpression(context);
        Expression rightExpr = RightHandValue.BuildExpression(context);
        return Expression.NotEqual(leftExpr, rightExpr);
    }

    public override string ToString() => $"{LeftHandValue} != {RightHandValue}";
}