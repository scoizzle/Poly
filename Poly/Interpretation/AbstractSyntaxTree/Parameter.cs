namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a parameter in an interpretation tree that will become a lambda parameter.
/// </summary>
/// <remarks>
/// Parameters are typed inputs to an expression tree that compile into <see cref="Exprs.ParameterExpression"/> nodes.
/// The parameter expression is created once and cached to ensure referential equality across multiple uses,
/// which is required for proper expression tree compilation.
/// Type information is resolved by semantic analysis middleware.
/// </remarks>
public sealed record Parameter(string Name, ITypeDefinition Type) : Node
{
    /// <inheritdoc />
    public override TResult Transform<TResult>(ITransformer<TResult> transformer) => transformer.Transform(this);

    /// <inheritdoc />
    public override string ToString() => $"{Type} {Name}";
}