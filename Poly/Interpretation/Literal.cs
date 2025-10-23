using Poly.Introspection;

namespace Poly.Interpretation;

public sealed class Literal(object? value) : Constant {
    public object? Value { get; } = value;

    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) {
        Type type = Value?.GetType() ?? typeof(object);
        return context.GetTypeDefinition(type) ??
            throw new InvalidOperationException($"Type '{type}' is not registered in the context.");
    }

    public override Expression BuildExpression(InterpretationContext context) => Expression.Constant(Value);

    public override string ToString() => Value?.ToString() ?? "null";

    public static readonly Literal Null = new(null);
    public static readonly Literal True = new(true);
    public static readonly Literal False = new(false);
}