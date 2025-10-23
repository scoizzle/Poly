
using Poly.Introspection;

namespace Poly.Interpretation;

public sealed class Parameter(string name, ITypeDefinition type) : Value
{
    private readonly ParameterExpression _expression = Expression.Parameter(type.ReflectedType, name);

    public string Name { get; } = name;
    public ITypeDefinition Type { get; } = type;

    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) => Type;

    public override ParameterExpression BuildExpression(InterpretationContext context) => _expression;

    public override string ToString() => $"{Type} {Name}";
}