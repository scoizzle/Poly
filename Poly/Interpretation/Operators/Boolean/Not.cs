namespace Poly.Interpretation.Operators.Boolean;

public sealed class Not(Value value) : BooleanOperator {
    public Value Value { get; init; } = value ?? throw new ArgumentNullException(nameof(value));

    public override Expression BuildExpression(Context context) {
        var innerExpression = Value.BuildExpression(context);
        return Expression.Not(innerExpression);
    }

    public override string ToString() => $"!{Value}";
}