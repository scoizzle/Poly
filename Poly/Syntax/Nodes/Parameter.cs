namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a parameter in an interpretation tree that will become a lambda parameter.
/// </summary>
/// <remarks>
/// Parameters are structural syntax nodes representing formal arguments to operations or functions.
/// Type information and semantic resolution are determined by analysis passes specific to each interpretation context.
/// </remarks>
public sealed record Parameter(string Name, Node? TypeReference = null, Node? DefaultValue = null) : Node {
    public override IEnumerable<Node?> Children => [TypeReference, DefaultValue];

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