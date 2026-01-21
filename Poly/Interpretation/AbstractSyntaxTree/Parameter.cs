namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a parameter in an interpretation tree that will become a lambda parameter.
/// </summary>
/// <remarks>
/// Parameters are structural syntax nodes containing only the parameter name and optional type hint.
/// The actual type definition is resolved by semantic analysis middleware and stored in the context.
/// The parameter expression is created once and cached to ensure referential equality across multiple uses,
/// which is required for proper expression tree compilation.
/// </remarks>
public sealed record Parameter(string Name, string? TypeHint = null) : Node
{
    /// <inheritdoc />
    public override string ToString() => TypeHint is not null ? $"{TypeHint} {Name}" : Name;
}