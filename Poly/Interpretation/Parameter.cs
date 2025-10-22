
using Poly.Introspection;

namespace Poly.Interpretation;

public sealed class Parameter(string name, Type type) : Value
{
    private readonly ParameterExpression _expression = Expression.Parameter(type, name);

    public string Name { get; } = name;
    public Type Type { get; } = type;

    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) => context.GetTypeDefinition(Type)
        ?? throw new InvalidOperationException($"Type '{Type}' is not registered in the context.");
        
    public override ParameterExpression BuildExpression(InterpretationContext context) => _expression;

    public override string ToString() => $"{Type} {Name}";
}