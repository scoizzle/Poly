namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a parameter in an interpretation tree that will become a lambda parameter.
/// </summary>
/// <remarks>
/// Parameters are structural syntax nodes containing only the parameter name and optional type hint.
/// The actual type definition is resolved by semantic analysis passes (INodeAnalyzer implementations) and stored in the context.
/// Expression caching for referential equality is handled by the interpretation middleware, not the node itself.
/// </remarks>
public sealed record Parameter(string Name, Node? TypeReference = null, Node? DefaultValue = null) : Node {
    public override IEnumerable<Node?> Children => [TypeReference, DefaultValue];

    /// <inheritdoc />
    public override string ToString()
    {
        StringBuilder sb = new();
        sb.Append(TypeReference != null ? $"{TypeReference} " : "");
        sb.Append(Name);
        if (DefaultValue != null) {
            sb.Append($" = {DefaultValue}");
        }
        return sb.ToString();
    }
}