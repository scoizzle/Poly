
using Poly.Introspection;

namespace Poly.Interpretation;

/// <summary>
/// Represents a parameter in an interpretation tree that will become a lambda parameter.
/// </summary>
/// <remarks>
/// Parameters are typed inputs to an expression tree that compile into <see cref="System.Linq.Expressions.ParameterExpression"/> nodes.
/// The parameter expression is created once and cached to ensure referential equality across multiple uses,
/// which is required for proper expression tree compilation.
/// </remarks>
public sealed class Parameter(string name, ITypeDefinition type) : Value {
    private readonly ParameterExpression _expression = Expression.Parameter(type.ReflectedType, name);

    /// <summary>
    /// Gets the name of the parameter.
    /// </summary>
    public string Name { get; } = name;
    
    /// <summary>
    /// Gets the type definition of the parameter.
    /// </summary>
    public ITypeDefinition Type { get; } = type;

    /// <inheritdoc />
    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) => Type;

    /// <inheritdoc />
    /// <returns>The cached <see cref="ParameterExpression"/> for this parameter.</returns>
    public override ParameterExpression BuildExpression(InterpretationContext context) => _expression;

    /// <inheritdoc />
    public override string ToString() => $"{Type} {Name}";
}