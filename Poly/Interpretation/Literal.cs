using Poly.Introspection;

namespace Poly.Interpretation;

public sealed class Literal(object? value) : Constant {
    public object? Value { get; } = value;

    public override ITypeDefinition GetTypeDefinition(Context context) =>
        context.GetTypeDefinition(Value?.GetType() ?? typeof(object))
        ?? throw new InvalidOperationException($"Type '{Value?.GetType()}' is not registered in the context.");

    public override Expression BuildExpression(Context context) => Expression.Constant(Value);

    public override string ToString() => Value?.ToString() ?? "null";

    public static readonly Literal Null = new(null);
    public static readonly Literal True = new(true);
    public static readonly Literal False = new(false);
}