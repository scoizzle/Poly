namespace Poly.Syntax.AbstractSyntaxTree;

public record TypeReference(string TypeName) : Node {
    public static TypeReference To<T>() => new(typeof(T).FullName!);
};