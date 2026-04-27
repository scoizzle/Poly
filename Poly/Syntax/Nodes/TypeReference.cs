namespace Poly.Syntax.Nodes;

public record TypeReference(string TypeName) : Node {
    public static TypeReference To<T>() => new(typeof(T).FullName!);
};