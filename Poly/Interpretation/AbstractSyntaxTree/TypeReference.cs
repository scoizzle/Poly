namespace Poly.Interpretation.AbstractSyntaxTree;

public sealed record TypeReference(string TypeName) : Node
{
    public static TypeReference To<T>() => new(typeof(T).FullName!);
};