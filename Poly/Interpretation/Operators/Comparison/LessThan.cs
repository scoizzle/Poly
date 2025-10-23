namespace Poly.Interpretation.Operators.Comparison;

public sealed class LessThan(Value leftHandValue, Value rightHandValue) : BooleanOperator {
    public Value LeftHandValue { get; } = leftHandValue ?? throw new ArgumentNullException(nameof(leftHandValue));
    public Value RightHandValue { get; } = rightHandValue ?? throw new ArgumentNullException(nameof(rightHandValue));

    public override Expression BuildExpression(InterpretationContext context) {
        Expression leftExpr = LeftHandValue.BuildExpression(context);
        Expression rightExpr = RightHandValue.BuildExpression(context);
        return Expression.LessThan(leftExpr, rightExpr);
    }

    public override string ToString() => $"{LeftHandValue} < {RightHandValue}";
}