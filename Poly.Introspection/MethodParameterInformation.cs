namespace Poly.Introspection;

public sealed record MethodParameterInformation(
    int Position,
    string Name,
    TypeInformation Type,
    bool IsOptional = false,
    object? DefaultValue = null
)
{
    internal void ToStringBuilder(StringBuilder sb, int tabCount = 0)
    {
        Debug.Assert(sb != null, "StringBuilder cannot be null.");
        Debug.Assert(tabCount >= 0, "Tab count must be non-negative.");

        sb.Append('\t', tabCount);
        sb.AppendLine($"param {Name}: {Type.Name};");
    }
}
