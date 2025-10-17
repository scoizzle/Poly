
using Poly.Introspection;

namespace Poly.Interpretation;

public sealed class Parameter(string name, Type type) : Value
{
    public string Name { get; } = name;
    public Type Type { get; } = type;

    public override ITypeDefinition GetTypeDefinition(Context context) => context.GetTypeDefinition(Type)
        ?? throw new InvalidOperationException($"Type '{Type}' is not registered in the context.");
        
    public override Expression BuildExpression(Context context) => Expression.Parameter(Type, Name);

    public override string ToString() => $"{Type} {Name}";
}