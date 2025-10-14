namespace Poly.Interpretation;

public sealed class Literal(object? value) : Constant {
    public object? Value { get; init; } = value;

    public override Expression BuildExpression(Context context) => Expression.Constant(Value);

    public override string ToString() => Value?.ToString() ?? "null";

    public static readonly Literal Null = new(null);
    public static readonly Literal True = new(true);
    public static readonly Literal False = new(false);
}
