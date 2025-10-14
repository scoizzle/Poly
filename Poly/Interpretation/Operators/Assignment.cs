namespace Poly.Interpretation;

public sealed class Assignment(Value destination, Value value) : Operator {
    public Value Destination { get; init; } = destination ?? throw new ArgumentNullException(nameof(destination));
    public Value Value { get; init; } = value ?? throw new ArgumentNullException(nameof(value));

    public override Expression BuildExpression(Context context) {
        Expression destExpr = Destination.BuildExpression(context);
        Expression valueExpr = Value.BuildExpression(context);
        return Expression.Assign(destExpr, valueExpr);
    }

    public override string ToString() => $"{Destination} = {Value}";
}