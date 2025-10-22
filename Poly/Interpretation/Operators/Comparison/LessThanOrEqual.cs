namespace Poly.Interpretation.Operators.Comparison;

public sealed class LessThanOrEqual(Value leftHandValue, Value rightHandValue) : BooleanOperator {
    public Value LeftHandValue { get; init; } = leftHandValue ?? throw new ArgumentNullException(nameof(leftHandValue));
    public Value RightHandValue { get; init; } = rightHandValue ?? throw new ArgumentNullException(nameof(rightHandValue));

    public override Expression BuildExpression(InterpretationContext context) {
        Expression leftExpr = LeftHandValue.BuildExpression(context);
        Expression rightExpr = RightHandValue.BuildExpression(context);
        return Expression.LessThanOrEqual(leftExpr, rightExpr);
    }

    public override string ToString() => $"{LeftHandValue} <= {RightHandValue}";
}