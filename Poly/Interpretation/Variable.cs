using Poly.Introspection;

namespace Poly.Interpretation;

public class Variable(string name, Value? value = null) : Value {
    public string Name { get; } = name;
    public Value? Value { get; set; } = value;

    public override ITypeDefinition GetTypeDefinition(Context context) => Value?.GetTypeDefinition(context)
        ?? throw new InvalidOperationException($"Variable '{Name}' is not initialized.");

    public override Expression BuildExpression(Context context) => Value?.BuildExpression(context)
        ?? throw new InvalidOperationException($"Variable '{Name}' is not initialized.");

    public override string ToString() => Name;
}
