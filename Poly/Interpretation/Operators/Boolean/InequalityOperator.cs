using Poly.Introspection;

namespace Poly.Interpretation.Operators.Boolean;

public sealed class InequalityOperator(Value leftHandValue, Value rightHandValue) : BooleanOperator {
    public Value LeftHandValue { get; init; } = leftHandValue ?? throw new ArgumentNullException(nameof(leftHandValue));
    public Value RightHandValue { get; init; } = rightHandValue ?? throw new ArgumentNullException(nameof(rightHandValue));

    public override Expression BuildExpression(Context context) {
        Expression leftExpr = LeftHandValue.BuildExpression(context);
        Expression rightExpr = RightHandValue.BuildExpression(context);
        return Expression.NotEqual(leftExpr, rightExpr);
    }

    public override string ToString() => $"{LeftHandValue} != {RightHandValue}";
}