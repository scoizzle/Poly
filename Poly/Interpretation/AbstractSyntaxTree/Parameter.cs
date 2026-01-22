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
public sealed record Parameter(string Name, Node? TypeReference = null, Node? DefaultValue = null) : Node
{
    /// <inheritdoc />
    public override string ToString() {
        StringBuilder sb = new();
        sb.Append(TypeReference != null ? $"{TypeReference} " : "");
        sb.Append(Name);
        if (DefaultValue != null) {
            sb.Append($" = {DefaultValue}");
        }
        return sb.ToString();
    }
}